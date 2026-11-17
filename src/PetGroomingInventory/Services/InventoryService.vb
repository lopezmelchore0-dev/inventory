Imports MySqlConnector

Public NotInheritable Class InventoryService
    Private Sub New()
    End Sub

    ''' <summary>Loads all items and computes their statuses with the current settings.</summary>
    Public Shared Function LoadSnapshot() As InventorySnapshot
        Using conn = Db.Open()
            Dim settings = SettingsRepository.Load(conn)
            Dim items = ItemRepository.GetAll(conn)
            ApplyStatuses(items, settings, Date.Today)
            Return New InventorySnapshot With {.Items = items, .Settings = settings, .Summary = Summarize(items)}
        End Using
    End Function

    Public Shared Function GetItems() As List(Of Item)
        Return LoadSnapshot().Items
    End Function

    Public Shared Sub ApplyStatuses(items As IEnumerable(Of Item), settings As AppSettings, today As Date)
        For Each it In items
            it.Status = StatusCalculator.GetStockStatus(it.Quantity, settings.LowStockThreshold, settings.CriticalStockThreshold)
            it.ExpiryStatus = StatusCalculator.GetExpirationStatus(it.ExpirationDate, today, settings.ExpiringSoonDays)
            If it.ExpirationDate.HasValue Then
                it.DaysToExpiration = CInt((it.ExpirationDate.Value.Date - today.Date).TotalDays)
            Else
                it.DaysToExpiration = Nothing
            End If
        Next
    End Sub

    Public Shared Function Summarize(items As IEnumerable(Of Item)) As DashboardSummary
        Return New DashboardSummary With {
            .TotalItems = items.Count(),
            .AvailableStock = items.Sum(Function(i) i.Quantity),
            .StockByUnit = Fmt.QtyByUnit(items.Select(Function(i) (Qty:=i.Quantity, Unit:=i.Unit))),
            .InStock = items.Count(Function(i) i.Status = StockStatus.InStock),
            .LowStock = items.Count(Function(i) i.Status = StockStatus.LowStock),
            .CriticalStock = items.Count(Function(i) i.Status = StockStatus.CriticalStock),
            .OutOfStock = items.Count(Function(i) i.Status = StockStatus.OutOfStock),
            .ExpiringSoon = items.Count(Function(i) i.ExpiryStatus = ExpirationStatus.ExpiringSoon),
            .Expired = items.Count(Function(i) i.ExpiryStatus = ExpirationStatus.Expired)
        }
    End Function

    Public Const MaxDescriptionLength As Integer = 500

    Public Const MaxSuggestionLength As Integer = 60

    ''' <summary>Suggestions for the Description box: the built-in examples first, then descriptions already used by items.</summary>
    Public Shared Function GetDescriptionSuggestions() As List(Of String)
        Dim used As List(Of String)
        Using conn = Db.Open()
            used = ItemRepository.GetDistinctDescriptions(conn, MaxSuggestionLength)
        End Using
        Return MergeSuggestions(DescriptionSuggestions.Defaults, used)
    End Function

    ''' <summary>Suggestions for the Unit box: the predefined units, "Other (Custom Unit)", then custom units already used by items.</summary>
    Public Shared Function GetUnitSuggestions() As List(Of String)
        Dim used As List(Of String)
        Using conn = Db.Open()
            used = ItemRepository.GetDistinctUnits(conn)
        End Using
        ' Used predefined units appear as their short value ("pcs"); show them once, as their label.
        Return MergeSuggestions(MeasurementUnits.Labels, used.Select(AddressOf MeasurementUnits.LabelFor))
    End Function

    ''' <summary>Suggestions for the Pet Type box: Dog, Cat, Both (Dog &amp; Cat), "Other (Custom Pet Type)", then custom pet types already used by items.</summary>
    Public Shared Function GetPetTypeSuggestions() As List(Of String)
        Dim defaults = PetTypes.All.Select(AddressOf PetTypes.EditorLabel).Concat({PetTypes.OtherLabel})
        Return MergeSuggestions(defaults, GetPetTypesInUse().Where(Function(p) Not PetTypes.IsBuiltIn(p)))
    End Function

    ''' <summary>The built-in pet types followed by the custom pet types used by items, for the pet type filters.</summary>
    Public Shared Function GetPetTypesInUse() As List(Of String)
        Using conn = Db.Open()
            Return PetTypes.Ordered(ItemRepository.GetDistinctPetTypes(conn, Nothing))
        End Using
    End Function

    ''' <summary>
    ''' A custom pet type that matches one already in use (ignoring case) takes the saved spelling, so "rabbit" and
    ''' "Rabbit" are not listed as two pet types.
    ''' </summary>
    Private Shared Function MatchExistingPetType(conn As MySqlConnection, tx As MySqlTransaction, petType As String) As String
        If petType Is Nothing OrElse PetTypes.IsBuiltIn(petType) Then Return petType
        Dim existing = ItemRepository.GetDistinctPetTypes(conn, tx).
            FirstOrDefault(Function(p) String.Equals(p, petType, StringComparison.OrdinalIgnoreCase))
        Return If(existing, petType)
    End Function

    ''' <summary>Defaults first, then the extras; duplicates (ignoring case) are dropped and the first spelling wins.</summary>
    Public Shared Function MergeSuggestions(defaults As IEnumerable(Of String), extras As IEnumerable(Of String)) As List(Of String)
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim result As New List(Of String)
        For Each entry In defaults.Concat(extras)
            Dim trimmed = If(entry, "").Trim()
            If trimmed.Length > 0 AndAlso seen.Add(trimmed) Then result.Add(trimmed)
        Next
        Return result
    End Function

    ''' <summary>Field-level validation shared by the form and the service. Keys: name, category, pettype, supplier, quantity, unit, price, expiration, description.</summary>
    Public Shared Function Validate(item As Item) As Dictionary(Of String, String)
        Dim errors As New Dictionary(Of String, String)
        Dim name = If(item.Name, "").Trim()
        If name.Length = 0 Then
            errors("name") = "Item name is required."
        ElseIf name.Length > 150 Then
            errors("name") = "Item name must be 150 characters or fewer."
        End If
        If item.CategoryId <= 0 Then errors("category") = "Select a category."
        Dim petType = If(item.PetType, "").Trim()
        If petType.Length = 0 Then
            errors("pettype") = "Select or type who this item is for (e.g. Dog, Cat, Rabbit)."
        ElseIf petType.Length > PetTypes.MaxLength Then
            errors("pettype") = $"Pet type must be {PetTypes.MaxLength} characters or fewer."
        End If
        If item.SupplierId <= 0 Then errors("supplier") = "Select a supplier (or add a new one)."

        Dim unit = If(item.Unit, "").Trim()
        If unit.Length = 0 Then
            errors("unit") = "Select a unit, or choose ""Other (Custom Unit)"" and type your own."
        ElseIf unit.Length > MeasurementUnits.MaxLength Then
            errors("unit") = $"Unit must be {MeasurementUnits.MaxLength} characters or fewer."
        End If

        Dim qtyError = InputValidator.CheckAmount(item.Quantity, "Quantity", allowZero:=True)
        If qtyError IsNot Nothing Then
            errors("quantity") = qtyError
        ElseIf InputValidator.CheckWholeForUnit(item.Quantity, unit, "Quantity") IsNot Nothing Then
            ' A new item's quantity is typed in the form; an existing item's stock can only change through stock in / adjustment.
            If item.Id = 0 Then
                errors("quantity") = InputValidator.CheckWholeForUnit(item.Quantity, unit, "Quantity")
            ElseIf Not errors.ContainsKey("unit") Then
                errors("unit") = $"The stock on hand ({Fmt.Qty(item.Quantity)}) is not a whole number, so it cannot be counted in " &
                                 $"{MeasurementUnits.Display(unit, 2D)}. Adjust the stock first, or choose another unit."
            End If
        End If
        Dim priceError = InputValidator.CheckAmount(item.RetailPrice, "Retail price", allowZero:=True)
        If priceError IsNot Nothing Then errors("price") = priceError

        If item.ExpirationDate.HasValue AndAlso (item.ExpirationDate.Value.Year < 2000 OrElse item.ExpirationDate.Value.Year > 2100) Then
            errors("expiration") = "Enter a realistic expiration date (years 2000–2100) or leave it blank."
        End If
        If If(item.Description, "").Trim().Length > MaxDescriptionLength Then
            errors("description") = $"Description must be {MaxDescriptionLength} characters or fewer."
        End If
        Return errors
    End Function

    Public Shared Function AddItem(input As Item) As Integer
        input.Unit = MeasurementUnits.Normalize(input.Unit)
        input.PetType = PetTypes.Normalize(input.PetType)
        Dim errors = Validate(input)
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Dim newId As Integer
        Try
            Using conn = Db.Open()
                Using tx = conn.BeginTransaction()
                    If ItemRepository.NameExists(conn, tx, input.Name, 0) Then
                        Throw New InputValidationException("name", "An item with this name already exists.")
                    End If
                    input.PetType = MatchExistingPetType(conn, tx, input.PetType)
                    Dim refs = LoadReferences(conn, tx, input)

                    newId = ItemRepository.Insert(conn, tx, input)
                    If input.Quantity > 0 Then
                        StockRepository.Insert(conn, tx, New StockTransaction With {
                            .ItemId = newId,
                            .ItemName = input.Name.Trim(),
                            .TxnType = TxnTypes.Initial,
                            .QtyChange = input.Quantity,
                            .QtyBefore = 0D,
                            .QtyAfter = input.Quantity,
                            .Unit = input.Unit,
                            .SupplierId = input.SupplierId,
                            .SupplierName = refs.Supplier,
                            .TxnDate = Date.Today,
                            .Note = "Opening stock when the item was added"
                        })
                    End If
                    ActivityRepository.Log(conn, tx, input.Name.Trim(), ActivityTypes.ItemAdded,
                        $"Added with {Fmt.Qty(input.Quantity, input.Unit)}{If(String.IsNullOrWhiteSpace(input.Description), "", " (" & input.Description.Trim() & ")")} at {Fmt.Money(input.RetailPrice)} ({refs.Category} · {PetTypes.Label(input.PetType)}, supplier: {refs.Supplier})")
                    tx.Commit()
                End Using
            End Using
        Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
            Throw New InputValidationException("name", "An item with this name already exists.")
        End Try

        AppEvents.RaiseDataChanged()
        Return newId
    End Function

    ''' <summary>Saves descriptive changes. Returns False when nothing changed. Quantity is never changed here.</summary>
    Public Shared Function UpdateItem(input As Item) As Boolean
        input.Unit = MeasurementUnits.Normalize(input.Unit)
        input.PetType = PetTypes.Normalize(input.PetType)
        Dim errors = Validate(input)
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Try
            Using conn = Db.Open()
                Using tx = conn.BeginTransaction()
                    Dim existing = ItemRepository.GetById(conn, tx, input.Id)
                    If existing Is Nothing Then Throw New BusinessRuleException("This item no longer exists. It may have been removed.")
                    If ItemRepository.NameExists(conn, tx, input.Name, input.Id) Then
                        Throw New InputValidationException("name", "Another item already uses this name.")
                    End If
                    ' Matched against the other items only, so this item's own spelling can still be changed (e.g. "rabbit" → "Rabbit").
                    If Not String.Equals(existing.PetType, input.PetType, StringComparison.OrdinalIgnoreCase) Then
                        input.PetType = MatchExistingPetType(conn, tx, input.PetType)
                    End If
                    Dim refs = LoadReferences(conn, tx, input)

                    Dim changes As New List(Of String)
                    Dim newName = input.Name.Trim()
                    If Not String.Equals(existing.Name, newName, StringComparison.Ordinal) Then changes.Add($"name ""{existing.Name}"" → ""{newName}""")
                    If existing.CategoryId <> input.CategoryId Then changes.Add($"category {existing.CategoryName} → {refs.Category}")
                    If existing.PetType <> input.PetType Then changes.Add($"pet type {PetTypes.Label(existing.PetType)} → {PetTypes.Label(input.PetType)}")
                    If Not String.Equals(If(existing.Unit, ""), If(input.Unit, ""), StringComparison.Ordinal) Then
                        ' Validate checked the form's copy of the quantity; recheck against the stored stock.
                        Dim unitError = InputValidator.CheckWholeForUnit(existing.Quantity, input.Unit, "Quantity")
                        If unitError IsNot Nothing Then
                            input.Quantity = existing.Quantity
                            Throw New InputValidationException(Validate(input))
                        End If
                        changes.Add($"unit {If(String.IsNullOrWhiteSpace(existing.Unit), "(none)", existing.Unit)} → {input.Unit} (quantity not converted)")
                    End If
                    If existing.RetailPrice <> input.RetailPrice Then changes.Add($"retail price {Fmt.Money(existing.RetailPrice)} → {Fmt.Money(input.RetailPrice)}")
                    If existing.SupplierId <> input.SupplierId Then changes.Add($"supplier {existing.SupplierName} → {refs.Supplier}")
                    If Not Nullable.Equals(existing.ExpirationDate, input.ExpirationDate) Then
                        changes.Add($"expiration {Fmt.DateText(existing.ExpirationDate)} → {Fmt.DateText(input.ExpirationDate)}")
                    End If
                    If Not String.Equals(If(existing.Description, "").Trim(), If(input.Description, "").Trim(), StringComparison.Ordinal) Then
                        changes.Add($"description ""{Fmt.Truncate(If(existing.Description, "").Trim(), 60)}"" → ""{Fmt.Truncate(If(input.Description, "").Trim(), 60)}""")
                    End If
                    If changes.Count = 0 Then Return False

                    ItemRepository.UpdateDetails(conn, tx, input)
                    ActivityRepository.Log(conn, tx, newName, ActivityTypes.ItemUpdated, "Changed " & String.Join("; ", changes))
                    tx.Commit()
                End Using
            End Using
        Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
            Throw New InputValidationException("name", "Another item already uses this name.")
        End Try

        AppEvents.RaiseDataChanged()
        Return True
    End Function

    Public Shared Sub DeleteItem(id As Integer)
        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                Dim existing = ItemRepository.GetById(conn, tx, id)
                If existing Is Nothing Then Throw New BusinessRuleException("This item no longer exists. It may have already been removed.")
                ItemRepository.Delete(conn, tx, id)
                ActivityRepository.Log(conn, tx, existing.Name, ActivityTypes.ItemRemoved,
                    $"Removed from inventory (had {existing.QuantityText},{existing.CategoryName})")
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
    End Sub

    ''' <summary>Confirms the selected category and supplier still exist and returns their names for the activity log.</summary>
    Private Shared Function LoadReferences(conn As MySqlConnection, tx As MySqlTransaction, item As Item) As (Category As String, Supplier As String)
        Dim errors As New Dictionary(Of String, String)
        Dim category = LookupRepository.GetName(conn, tx, LookupKind.Category, item.CategoryId)
        If category Is Nothing Then errors("category") = "The selected category no longer exists."
        Dim supplier = SupplierRepository.GetById(conn, tx, item.SupplierId)
        If supplier Is Nothing Then errors("supplier") = "The selected supplier no longer exists."
        If errors.Count > 0 Then Throw New InputValidationException(errors)
        Return (category, supplier.Name)
    End Function
End Class
