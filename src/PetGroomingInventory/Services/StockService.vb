Imports MySqlConnector

''' <summary>Stock-in and stock adjustments: the only ways an item's quantity changes after it is added.</summary>
Public NotInheritable Class StockService
    Private Sub New()
    End Sub

    Public Const OtherReason As String = "Other"

    Public Shared ReadOnly AdjustmentReasons As String() = {
        "Count correction",
        "Damaged",
        "Expired / disposed",
        "Used in grooming services",
        "Lost / missing",
        "Returned to supplier",
        OtherReason
    }

    Public Shared Function ComputeNewQuantity(current As Decimal, mode As AdjustmentMode, amount As Decimal) As Decimal
        Select Case mode
            Case AdjustmentMode.Add : Return current + amount
            Case AdjustmentMode.Deduct : Return current - amount
            Case Else : Return amount
        End Select
    End Function

    ''' <summary>
    ''' Validates an adjustment against the current quantity. <paramref name="unit"/> is the item's unit: amounts are
    ''' always in that unit (never converted), and counted units take whole numbers only. Keys: amount, reason, note.
    ''' </summary>
    Public Shared Function ValidateAdjustment(current As Decimal, mode As AdjustmentMode, amount As Decimal,
                                              reason As String, note As String, Optional unit As String = Nothing) As Dictionary(Of String, String)
        Dim errors As New Dictionary(Of String, String)
        Dim label = If(mode = AdjustmentMode.SetCount, "Counted quantity", "Quantity")
        Dim amountError = If(InputValidator.CheckAmount(amount, label, allowZero:=(mode = AdjustmentMode.SetCount)),
                             InputValidator.CheckWholeForUnit(amount, unit, label))
        If amountError IsNot Nothing Then
            errors("amount") = amountError
        Else
            Dim newQty = ComputeNewQuantity(current, mode, amount)
            If newQty < 0 Then
                errors("amount") = $"You can deduct at most {Fmt.Qty(current, unit)} (the available stock)."
            ElseIf newQty = current Then
                errors("amount") = "This adjustment does not change the stock."
            ElseIf newQty > InputValidator.MaxAmount Then
                errors("amount") = $"Stock cannot exceed {InputValidator.MaxAmount:N0}."
            End If
        End If
        If String.IsNullOrWhiteSpace(reason) OrElse Not AdjustmentReasons.Contains(reason) Then
            errors("reason") = "Select a reason for this adjustment."
        ElseIf reason = OtherReason AndAlso String.IsNullOrWhiteSpace(note) Then
            errors("note") = "Describe the reason when you choose ""Other""."
        End If
        If note IsNot Nothing AndAlso note.Trim().Length > 255 Then errors("note") = "Note must be 255 characters or fewer."
        Return errors
    End Function

    ''' <summary>
    ''' Checks that a stock change is recorded in the item's registered unit. <paramref name="expectedUnit"/> is the unit
    ''' the user saw when entering the quantity. Items without a unit cannot take stock changes until one is set.
    ''' Returns an error message, or Nothing.
    ''' </summary>
    Public Shared Function CheckUnit(registeredUnit As String, expectedUnit As String) As String
        If String.IsNullOrWhiteSpace(registeredUnit) Then
            Return "This item has no unit yet. Set its unit in Inventory › Edit item before recording stock."
        End If
        If Not String.Equals(MeasurementUnits.Normalize(expectedUnit), registeredUnit.Trim(), StringComparison.Ordinal) Then
            Return $"This item's unit was changed to ""{MeasurementUnits.Display(registeredUnit, 2D)}"" after this form was opened. " &
                   "Close and reopen the form, then check the quantity."
        End If
        Return Nothing
    End Function

    Public Shared Sub StockIn(itemId As Integer, quantity As Decimal, receivedDate As Date, supplierId As Integer?, note As String,
                              expectedUnit As String)
        Dim errors As New Dictionary(Of String, String)
        If itemId <= 0 Then errors("item") = "Select the item you received."
        Dim qtyError = InputValidator.CheckAmount(quantity, "Quantity received", allowZero:=False)
        If qtyError IsNot Nothing Then errors("quantity") = qtyError
        If receivedDate.Date > Date.Today Then
            errors("date") = "The date received cannot be in the future."
        ElseIf receivedDate.Date < Date.Today.AddYears(-2) Then
            errors("date") = "The date received is more than 2 years ago. Check the date."
        End If
        If note IsNot Nothing AndAlso note.Trim().Length > 255 Then errors("note") = "Note must be 255 characters or fewer."
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                ' Lock the row first so the unit read below is the current one.
                Dim current = ItemRepository.LockQuantity(conn, tx, itemId)
                Dim item = ItemRepository.GetById(conn, tx, itemId)
                If item Is Nothing OrElse Not current.HasValue Then Throw New InputValidationException("item", "This item no longer exists.")
                Dim unitError = CheckUnit(item.Unit, expectedUnit)
                If unitError IsNot Nothing Then Throw New InputValidationException("unit", unitError)
                Dim wholeError = InputValidator.CheckWholeForUnit(quantity, item.Unit, "Quantity received")
                If wholeError IsNot Nothing Then Throw New InputValidationException("quantity", wholeError)

                Dim supplierName As String = Nothing
                If supplierId.HasValue Then
                    Dim supplier = SupplierRepository.GetById(conn, tx, supplierId.Value)
                    If supplier Is Nothing Then Throw New InputValidationException("supplier", "The selected supplier no longer exists.")
                    supplierName = supplier.Name
                End If

                Dim newQty = current.Value + quantity
                If newQty > InputValidator.MaxAmount Then
                    Throw New InputValidationException("quantity", $"Stock cannot exceed {InputValidator.MaxAmount:N0}.")
                End If

                ItemRepository.UpdateQuantity(conn, tx, itemId, newQty)
                StockRepository.Insert(conn, tx, New StockTransaction With {
                    .ItemId = itemId,
                    .ItemName = item.Name,
                    .TxnType = TxnTypes.StockIn,
                    .QtyChange = quantity,
                    .QtyBefore = current.Value,
                    .QtyAfter = newQty,
                    .Unit = item.Unit,
                    .SupplierId = supplierId,
                    .SupplierName = supplierName,
                    .TxnDate = receivedDate.Date,
                    .Note = note
                })

                Dim description = $"Received {Fmt.Qty(quantity, item.Unit)}"
                If supplierName IsNot Nothing Then description &= $" from {supplierName}"
                description &= $" ({Fmt.Qty(current.Value)} → {Fmt.Qty(newQty, item.Unit)})"
                If receivedDate.Date <> Date.Today Then description &= $", dated {Fmt.DateText(receivedDate)}"
                If Not String.IsNullOrWhiteSpace(note) Then description &= $". Note: {note.Trim()}"
                ActivityRepository.Log(conn, tx, item.Name, ActivityTypes.StockReceived, description)
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
    End Sub

    ''' <summary>Applies an adjustment and returns the new quantity.</summary>
    Public Shared Function Adjust(itemId As Integer, mode As AdjustmentMode, amount As Decimal, reason As String, note As String,
                                  expectedUnit As String) As Decimal
        If itemId <= 0 Then Throw New InputValidationException("item", "Select the item to adjust.")
        Dim newQty As Decimal

        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                ' Lock the row first so the unit read below is the current one.
                Dim current = ItemRepository.LockQuantity(conn, tx, itemId)
                Dim item = ItemRepository.GetById(conn, tx, itemId)
                If item Is Nothing OrElse Not current.HasValue Then Throw New InputValidationException("item", "This item no longer exists.")
                Dim unitError = CheckUnit(item.Unit, expectedUnit)
                If unitError IsNot Nothing Then Throw New InputValidationException("unit", unitError)

                Dim errors = ValidateAdjustment(current.Value, mode, amount, reason, note, item.Unit)
                If errors.Count > 0 Then Throw New InputValidationException(errors)

                newQty = ComputeNewQuantity(current.Value, mode, amount)
                Dim change = newQty - current.Value
                ItemRepository.UpdateQuantity(conn, tx, itemId, newQty)
                StockRepository.Insert(conn, tx, New StockTransaction With {
                    .ItemId = itemId,
                    .ItemName = item.Name,
                    .TxnType = TxnTypes.Adjustment,
                    .QtyChange = change,
                    .QtyBefore = current.Value,
                    .QtyAfter = newQty,
                    .Unit = item.Unit,
                    .TxnDate = Date.Today,
                    .Reason = reason,
                    .Note = note
                })

                Dim verb = If(change > 0, "Added", "Deducted")
                Dim description = $"{verb} {Fmt.Qty(Math.Abs(change), item.Unit)} — {reason} ({Fmt.Qty(current.Value)} → {Fmt.Qty(newQty, item.Unit)})"
                If Not String.IsNullOrWhiteSpace(note) Then description &= $". Note: {note.Trim()}"
                ActivityRepository.Log(conn, tx, item.Name, ActivityTypes.StockAdjusted, description)
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
        Return newQty
    End Function

    Public Shared Function GetHistory(adjustments As Boolean, Optional limit As Integer = 500) As List(Of StockTransaction)
        Using conn = Db.Open()
            Return StockRepository.GetHistory(conn, adjustments, limit)
        End Using
    End Function
End Class
