''' <summary>
''' Builds inventory reports from the data the app actually stores: current items, stock transactions and settings.
''' Nothing is estimated or invented: reports that need past stock levels (which are not stored) show current stock and say so.
''' Build is pure (no database) so every report can be unit-tested.
''' </summary>
Public NotInheritable Class ReportService
    Private Sub New()
    End Sub

    Public Const MinYear As Integer = 2000
    Public Const MaxYear As Integer = 2100

    Private Const RetailValueNote As String = "Retail value = quantity × retail price. The system does not store cost prices."

    ''' <summary>Field errors keyed "from" and "to"; empty when the criteria can be used.</summary>
    Public Shared Function Validate(criteria As ReportCriteria) As Dictionary(Of String, String)
        Dim errors As New Dictionary(Of String, String)
        If criteria Is Nothing OrElse Not ReportTypes.All.Contains(criteria.Type) Then
            errors("type") = "Select a report type."
            Return errors
        End If
        If Not ReportTypes.UsesDateRange(criteria.Type) Then Return errors

        CheckDate(errors, "from", criteria.FromDate, "Inventory From")
        CheckDate(errors, "to", criteria.ToDate, "Inventory To")
        If errors.Count = 0 AndAlso criteria.ToDate.Value.Date < criteria.FromDate.Value.Date Then
            errors("to") = "Inventory To cannot be earlier than Inventory From."
        End If
        Return errors
    End Function

    Private Shared Sub CheckDate(errors As Dictionary(Of String, String), key As String, value As Date?, label As String)
        If Not value.HasValue Then
            errors(key) = $"Select the {label} date."
        ElseIf value.Value.Year < MinYear OrElse value.Value.Year > MaxYear Then
            errors(key) = $"Enter a {label} date between {MinYear} and {MaxYear}."
        End If
    End Sub

    ''' <summary>Validates, loads the data the report needs and builds it.</summary>
    Public Shared Function Generate(criteria As ReportCriteria) As ReportResult
        Dim errors = Validate(criteria)
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Using conn = Db.Open()
            Dim settings = SettingsRepository.Load(conn)
            Dim items = ItemRepository.GetAll(conn)
            Dim categories = LookupRepository.GetAll(conn, LookupKind.Category)
            Dim transactions As New List(Of StockTransaction)
            If criteria.Type = ReportType.Summary OrElse criteria.Type = ReportType.Movement Then
                transactions = StockRepository.GetByDateRange(conn, criteria.FromDate.Value, criteria.ToDate.Value)
            End If
            Return Build(criteria, items, transactions, settings, categories, Date.Now)
        End Using
    End Function

    ''' <summary>Earliest and latest dates in the data, for the "All time" range.</summary>
    Public Shared Function GetDateBounds() As (Earliest As Date?, Latest As Date?)
        Using conn = Db.Open()
            Return StockRepository.GetDateBounds(conn)
        End Using
    End Function

    Public Shared Function Build(criteria As ReportCriteria, allItems As List(Of Item), transactions As List(Of StockTransaction),
                                 settings As AppSettings, categories As List(Of LookupItem), now As Date) As ReportResult
        InventoryService.ApplyStatuses(allItems, settings, now.Date)
        Dim items = allItems.Where(Function(i) MatchesFilters(i, criteria)).
                             OrderBy(Function(i) i.Name, StringComparer.CurrentCultureIgnoreCase).ToList()

        Dim result As New ReportResult With {
            .Criteria = criteria,
            .ShopName = settings.AppName,
            .Title = ReportTypes.Title(criteria.Type),
            .GeneratedAt = now,
            .DateBasisNote = ReportTypes.DateBasis(criteria.Type),
            .FilterText = DescribeFilters(criteria, categories),
            .PeriodText = If(ReportTypes.UsesDateRange(criteria.Type),
                             ReportFormat.Period(criteria.FromDate.Value, criteria.ToDate.Value),
                             $"Current stock as of {Fmt.DateTimeText(now)}")
        }

        Select Case criteria.Type
            Case ReportType.InventoryList : BuildInventoryList(result, items, criteria)
            Case ReportType.LowStock : BuildLowStock(result, items, settings)
            Case ReportType.OutOfStock : BuildOutOfStock(result, items)
            Case ReportType.Expiring : BuildExpiring(result, items, criteria, settings)
            Case ReportType.Category : BuildCategory(result, items, criteria, categories)
            Case ReportType.PetType : BuildPetType(result, items, criteria)
            Case ReportType.Movement : BuildMovement(result, allItems, transactions, criteria)
            Case Else : BuildSummary(result, items, allItems, transactions, criteria, now)
        End Select
        Return result
    End Function

    ' ===================== Reports =====================

    Private Shared Sub BuildSummary(result As ReportResult, items As List(Of Item), allItems As List(Of Item),
                                    transactions As List(Of StockTransaction), criteria As ReportCriteria, now As Date)
        Dim current = $"Current inventory (as of {Fmt.DateTimeText(now)})"
        Dim summary = InventoryService.Summarize(items)
        result.Stats.AddRange({
            New ReportStat(current, "Total items", summary.TotalItems, ReportValueKind.Count),
            New ReportStat(current, "Total stock quantity", summary.AvailableStock, ReportValueKind.Quantity),
            New ReportStat(current, "Total retail value", items.Sum(Function(i) i.RetailValue), ReportValueKind.Money),
            New ReportStat(current, "In stock", summary.InStock, ReportValueKind.Count),
            New ReportStat(current, "Low stock", summary.LowStock, ReportValueKind.Count),
            New ReportStat(current, "Critical stock", summary.CriticalStock, ReportValueKind.Count),
            New ReportStat(current, "Out of stock", summary.OutOfStock, ReportValueKind.Count)
        })

        Dim movement = FilterTransactions(allItems, transactions, criteria)
        Dim period = $"Period activity ({result.PeriodText})"
        Dim adjustments = movement.Rows.Where(Function(t) t.TxnType = TxnTypes.Adjustment).ToList()
        result.Stats.AddRange({
            New ReportStat(period, "Items added", items.Where(Function(i) InPeriod(i.CreatedAt, criteria)).Count(), ReportValueKind.Count),
            New ReportStat(period, "Stock received", movement.Rows.Where(Function(t) t.TxnType <> TxnTypes.Adjustment).Sum(Function(t) t.QtyChange), ReportValueKind.Quantity),
            New ReportStat(period, "Added by adjustments", adjustments.Where(Function(t) t.QtyChange > 0).Sum(Function(t) t.QtyChange), ReportValueKind.Quantity),
            New ReportStat(period, "Deducted by adjustments", Math.Abs(adjustments.Where(Function(t) t.QtyChange < 0).Sum(Function(t) t.QtyChange)), ReportValueKind.Quantity),
            New ReportStat(period, "Stock transactions", movement.Rows.Count, ReportValueKind.Count)
        })

        Dim byCategory = GroupTable("Current stock by category", "Category")
        For Each group In items.GroupBy(Function(i) i.CategoryName).OrderBy(Function(g) g.Key, StringComparer.CurrentCultureIgnoreCase)
            byCategory.Rows.Add(GroupRow(group.Key, group))
        Next
        byCategory.Totals = GroupRow("Total", items)

        Dim byPetType = GroupTable("Current stock by pet type", "Pet Type")
        For Each petType In PetTypes.All.Where(Function(p) PetTypes.Matches(p, criteria.PetType))
            byPetType.Rows.Add(GroupRow(PetTypes.Label(petType), items.Where(Function(i) i.PetType = petType)))
        Next
        byPetType.Totals = GroupRow("Total", items)

        result.Tables.Add(byCategory)
        result.Tables.Add(byPetType)
        result.IsEmpty = items.Count = 0 AndAlso movement.Rows.Count = 0
        result.Notes.Add("Current inventory figures are the stock on hand when the report was generated. The system does not keep past stock levels, so they are not limited to the selected period.")
        result.Notes.Add(RetailValueNote)
        result.Notes.AddRange(movement.Notes)
    End Sub

    Private Shared Sub BuildInventoryList(result As ReportResult, items As List(Of Item), criteria As ReportCriteria)
        Dim rows = items.Where(Function(i) InPeriod(i.CreatedAt, criteria)).ToList()
        Dim table As New ReportTable With {.Columns = New List(Of ReportColumn) From {
            New ReportColumn("Item Name", ReportValueKind.Text, 2),
            New ReportColumn("Category", ReportValueKind.Text, 1.3),
            New ReportColumn("Pet Type", ReportValueKind.Text, 0.9),
            New ReportColumn("Description", ReportValueKind.Text, 1.3),
            New ReportColumn("Quantity", ReportValueKind.Quantity, 0.8),
            New ReportColumn("Price", ReportValueKind.Money, 0.9),
            New ReportColumn("Retail Value", ReportValueKind.Money, 1),
            New ReportColumn("Supplier", ReportValueKind.Text, 1.3),
            New ReportColumn("Expiration Date", ReportValueKind.Date, 1),
            New ReportColumn("Stock Status", ReportValueKind.Text, 1),
            New ReportColumn("Date Added", ReportValueKind.Date, 1)
        }}
        For Each i In rows
            table.Rows.Add(New Object() {i.Name, i.CategoryName, i.PetTypeText, DescriptionText(i), i.Quantity, i.RetailPrice, i.RetailValue,
                                         i.SupplierName, i.ExpirationDate, i.StatusText, i.CreatedAt.Date})
        Next
        table.Totals = New Object() {"Total", Nothing, Nothing, Nothing, rows.Sum(Function(i) i.Quantity), Nothing, rows.Sum(Function(i) i.RetailValue),
                                     Nothing, Nothing, Nothing, Nothing}
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Items added in period", rows.Count, ReportValueKind.Count),
            New ReportStat("", "Total stock quantity", rows.Sum(Function(i) i.Quantity), ReportValueKind.Quantity),
            New ReportStat("", "Total retail value", rows.Sum(Function(i) i.RetailValue), ReportValueKind.Money),
            New ReportStat("", "Low / critical stock", rows.Where(Function(i) i.Status = StockStatus.LowStock OrElse i.Status = StockStatus.CriticalStock).Count(), ReportValueKind.Count),
            New ReportStat("", "Out of stock", rows.Where(Function(i) i.Status = StockStatus.OutOfStock).Count(), ReportValueKind.Count)
        })
        result.IsEmpty = rows.Count = 0
        result.EmptyMessage = "No items were added in the selected period with the selected filters. Try a wider date range, such as All Time."
        result.Notes.Add("Quantity, price and stock status are current values.")
        result.Notes.Add(RetailValueNote)
    End Sub

    Private Shared Sub BuildLowStock(result As ReportResult, items As List(Of Item), settings As AppSettings)
        Dim rows = items.Where(Function(i) i.Status = StockStatus.LowStock OrElse i.Status = StockStatus.CriticalStock).
                         OrderBy(Function(i) i.Quantity).ToList()
        Dim table As New ReportTable With {.Columns = New List(Of ReportColumn) From {
            New ReportColumn("Item Name", ReportValueKind.Text, 2),
            New ReportColumn("Category", ReportValueKind.Text, 1.3),
            New ReportColumn("Pet Type", ReportValueKind.Text, 0.9),
            New ReportColumn("Description", ReportValueKind.Text, 1.4),
            New ReportColumn("Current Quantity", ReportValueKind.Quantity, 1),
            New ReportColumn("Minimum Stock Level", ReportValueKind.Count, 1.1),
            New ReportColumn("Stock Status", ReportValueKind.Text, 1)
        }}
        For Each i In rows
            table.Rows.Add(New Object() {i.Name, i.CategoryName, i.PetTypeText, DescriptionText(i), i.Quantity, settings.LowStockThreshold, i.StatusText})
        Next
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Items at or below minimum", rows.Count, ReportValueKind.Count),
            New ReportStat("", "Critical stock", rows.Where(Function(i) i.Status = StockStatus.CriticalStock).Count(), ReportValueKind.Count),
            New ReportStat("", "Minimum stock level (Low threshold)", settings.LowStockThreshold, ReportValueKind.Count),
            New ReportStat("", "Critical threshold", settings.CriticalStockThreshold, ReportValueKind.Count)
        })
        result.IsEmpty = rows.Count = 0
        result.EmptyMessage = "No items are at or below the minimum stock level with the selected filters."
        result.Notes.Add($"The minimum stock level is the Low Stock threshold set in Settings ({settings.LowStockThreshold}). The system has no separate minimum for each item. " &
                         $"Items at or below the Critical threshold ({settings.CriticalStockThreshold}) are marked Critical Stock.")
        result.Notes.Add("Items with zero stock are listed in the Out-of-Stock Report.")
    End Sub

    Private Shared Sub BuildOutOfStock(result As ReportResult, items As List(Of Item))
        Dim rows = items.Where(Function(i) i.Status = StockStatus.OutOfStock).ToList()
        Dim table As New ReportTable With {.Columns = New List(Of ReportColumn) From {
            New ReportColumn("Item Name", ReportValueKind.Text, 2),
            New ReportColumn("Category", ReportValueKind.Text, 1.3),
            New ReportColumn("Pet Type", ReportValueKind.Text, 0.9),
            New ReportColumn("Description", ReportValueKind.Text, 1.4),
            New ReportColumn("Supplier", ReportValueKind.Text, 1.4),
            New ReportColumn("Current Quantity", ReportValueKind.Quantity, 1)
        }}
        For Each i In rows
            table.Rows.Add(New Object() {i.Name, i.CategoryName, i.PetTypeText, DescriptionText(i), i.SupplierName, i.Quantity})
        Next
        result.Tables.Add(table)
        result.Stats.Add(New ReportStat("", "Out-of-stock items", rows.Count, ReportValueKind.Count))
        result.IsEmpty = rows.Count = 0
        result.EmptyMessage = "No items are out of stock with the selected filters."
    End Sub

    Private Shared Sub BuildExpiring(result As ReportResult, items As List(Of Item), criteria As ReportCriteria, settings As AppSettings)
        Dim rows = items.Where(Function(i) i.ExpirationDate.HasValue AndAlso InPeriod(i.ExpirationDate.Value, criteria)).
                         OrderBy(Function(i) i.ExpirationDate.Value).ToList()
        Dim table As New ReportTable With {.Columns = New List(Of ReportColumn) From {
            New ReportColumn("Item Name", ReportValueKind.Text, 2),
            New ReportColumn("Category", ReportValueKind.Text, 1.3),
            New ReportColumn("Pet Type", ReportValueKind.Text, 0.9),
            New ReportColumn("Quantity", ReportValueKind.Quantity, 0.8),
            New ReportColumn("Expiration Date", ReportValueKind.Date, 1),
            New ReportColumn("Expiration Status", ReportValueKind.Text, 1.1),
            New ReportColumn("Time Left", ReportValueKind.Text, 1.1)
        }}
        For Each i In rows
            table.Rows.Add(New Object() {i.Name, i.CategoryName, i.PetTypeText, i.Quantity, i.ExpirationDate, i.ExpirationLabel, i.ExpirationDetail})
        Next
        table.Totals = New Object() {"Total", Nothing, Nothing, rows.Sum(Function(i) i.Quantity), Nothing, Nothing, Nothing}
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Expired", rows.Where(Function(i) i.ExpiryStatus = ExpirationStatus.Expired).Count(), ReportValueKind.Count),
            New ReportStat("", $"Expiring soon (within {settings.ExpiringSoonDays} days)", rows.Where(Function(i) i.ExpiryStatus = ExpirationStatus.ExpiringSoon).Count(), ReportValueKind.Count),
            New ReportStat("", "Expiring later", rows.Where(Function(i) i.ExpiryStatus = ExpirationStatus.Ok).Count(), ReportValueKind.Count),
            New ReportStat("", "Quantity affected", rows.Sum(Function(i) i.Quantity), ReportValueKind.Quantity)
        })
        result.IsEmpty = rows.Count = 0
        result.EmptyMessage = "No items expire within the selected date range with the selected filters."
        result.Notes.Add($"Expired = the expiration date has passed. Expiring Soon = expires within {settings.ExpiringSoonDays} days, counting today (set in Settings).")
        result.Notes.Add("Items without an expiration date are not included.")
    End Sub

    Private Shared Sub BuildCategory(result As ReportResult, items As List(Of Item), criteria As ReportCriteria, categories As List(Of LookupItem))
        Dim table = GroupTable(Nothing, "Category")
        For Each category In categories.Where(Function(c) Not criteria.CategoryId.HasValue OrElse c.Id = criteria.CategoryId.Value).
                                        OrderBy(Function(c) c.Name, StringComparer.CurrentCultureIgnoreCase)
            table.Rows.Add(GroupRow(category.Name, items.Where(Function(i) i.CategoryId = category.Id)))
        Next
        table.Totals = GroupRow("Total", items)
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Categories with items", items.Select(Function(i) i.CategoryId).Distinct().Count(), ReportValueKind.Count),
            New ReportStat("", "Total items", items.Count, ReportValueKind.Count),
            New ReportStat("", "Total stock quantity", items.Sum(Function(i) i.Quantity), ReportValueKind.Quantity),
            New ReportStat("", "Total retail value", items.Sum(Function(i) i.RetailValue), ReportValueKind.Money)
        })
        result.IsEmpty = items.Count = 0
        result.EmptyMessage = "No items match the selected category and pet type."
        result.Notes.Add("Categories come from Settings. Categories with no matching items are listed with zero.")
        result.Notes.Add(RetailValueNote)
    End Sub

    Private Shared Sub BuildPetType(result As ReportResult, items As List(Of Item), criteria As ReportCriteria)
        Dim table = GroupTable(Nothing, "Pet Type")
        For Each petType In PetTypes.All.Where(Function(p) PetTypes.Matches(p, criteria.PetType))
            table.Rows.Add(GroupRow(PetTypes.Label(petType), items.Where(Function(i) i.PetType = petType)))
        Next
        table.Totals = GroupRow("Total", items)
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Total items", items.Count, ReportValueKind.Count),
            New ReportStat("", "Usable for dogs (Dog + Dog & Cat)", items.Where(Function(i) i.PetType <> PetTypes.Cat).Count(), ReportValueKind.Count),
            New ReportStat("", "Usable for cats (Cat + Dog & Cat)", items.Where(Function(i) i.PetType <> PetTypes.Dog).Count(), ReportValueKind.Count),
            New ReportStat("", "Total retail value", items.Sum(Function(i) i.RetailValue), ReportValueKind.Money)
        })
        result.IsEmpty = items.Count = 0
        result.EmptyMessage = "No items match the selected category and pet type."
        result.Notes.Add("Each item is counted once, under its own pet type. ""Dog & Cat"" items are not added again to the Dog or Cat rows, so the totals have no double counting.")
        result.Notes.Add(RetailValueNote)
    End Sub

    Private Shared Sub BuildMovement(result As ReportResult, allItems As List(Of Item), transactions As List(Of StockTransaction), criteria As ReportCriteria)
        Dim movement = FilterTransactions(allItems, transactions, criteria)
        Dim table As New ReportTable With {.Columns = New List(Of ReportColumn) From {
            New ReportColumn("Date", ReportValueKind.Date, 0.9),
            New ReportColumn("Item Name", ReportValueKind.Text, 1.8),
            New ReportColumn("Transaction Type", ReportValueKind.Text, 1.2),
            New ReportColumn("Qty Added / Deducted", ReportValueKind.SignedQuantity, 1),
            New ReportColumn("Previous Qty", ReportValueKind.Quantity, 0.8),
            New ReportColumn("Updated Qty", ReportValueKind.Quantity, 0.8),
            New ReportColumn("Reason", ReportValueKind.Text, 1.1),
            New ReportColumn("Notes", ReportValueKind.Text, 1.5)
        }}
        For Each t In movement.Rows
            table.Rows.Add(New Object() {t.TxnDate.Date, t.ItemName, MovementLabel(t), t.QtyChange, t.QtyBefore, t.QtyAfter,
                                         If(String.IsNullOrWhiteSpace(t.Reason), "—", t.Reason), t.NoteText})
        Next
        Dim added = movement.Rows.Where(Function(t) t.QtyChange > 0).Sum(Function(t) t.QtyChange)
        Dim deducted = Math.Abs(movement.Rows.Where(Function(t) t.QtyChange < 0).Sum(Function(t) t.QtyChange))
        table.Totals = New Object() {"Net change", Nothing, Nothing, added - deducted, Nothing, Nothing, Nothing, Nothing}
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Transactions", movement.Rows.Count, ReportValueKind.Count),
            New ReportStat("", "Stock received", movement.Rows.Where(Function(t) t.TxnType <> TxnTypes.Adjustment).Sum(Function(t) t.QtyChange), ReportValueKind.Quantity),
            New ReportStat("", "Total added", added, ReportValueKind.Quantity),
            New ReportStat("", "Total deducted", deducted, ReportValueKind.Quantity),
            New ReportStat("", "Net change", added - deducted, ReportValueKind.SignedQuantity)
        })
        result.IsEmpty = movement.Rows.Count = 0
        result.EmptyMessage = "No stock movements were recorded in the selected period with the selected filters."
        result.Notes.Add("Only recorded stock transactions are listed: opening stock, Stock In and Stock Adjustment.")
        result.Notes.AddRange(movement.Notes)
    End Sub

    ' ===================== Helpers =====================

    Private Shared Function MatchesFilters(item As Item, criteria As ReportCriteria) As Boolean
        If criteria.CategoryId.HasValue AndAlso item.CategoryId <> criteria.CategoryId.Value Then Return False
        Return PetTypes.Matches(item.PetType, criteria.PetType)
    End Function

    Private Shared Function InPeriod(value As Date, criteria As ReportCriteria) As Boolean
        Return value.Date >= criteria.FromDate.Value.Date AndAlso value.Date <= criteria.ToDate.Value.Date
    End Function

    ''' <summary>
    ''' Transactions in the period that match the filters. Category and pet type are looked up on the current item;
    ''' records of removed items keep only their name, so they are left out whenever one of those filters is set.
    ''' </summary>
    Private Shared Function FilterTransactions(allItems As List(Of Item), transactions As List(Of StockTransaction),
                                               criteria As ReportCriteria) As (Rows As List(Of StockTransaction), Notes As List(Of String))
        Dim byId = allItems.ToDictionary(Function(i) i.Id)
        Dim filtered = criteria.CategoryId.HasValue OrElse criteria.PetType IsNot Nothing
        Dim rows As New List(Of StockTransaction)
        Dim removedShown = 0
        Dim removedSkipped = 0
        For Each t In transactions.Where(Function(x) InPeriod(x.TxnDate, criteria)).
                                   OrderBy(Function(x) x.TxnDate).ThenBy(Function(x) x.CreatedAt).ThenBy(Function(x) x.Id)
            Dim item As Item = Nothing
            Dim known = t.ItemId.HasValue AndAlso byId.TryGetValue(t.ItemId.Value, item)
            If Not known Then
                If filtered Then
                    removedSkipped += 1
                    Continue For
                End If
                removedShown += 1
            ElseIf Not MatchesFilters(item, criteria) Then
                Continue For
            End If
            rows.Add(t)
        Next

        Dim notes As New List(Of String)
        If removedShown > 0 Then
            notes.Add($"{Fmt.Plural(removedShown, "stock record")} belong to items that were later removed; they are listed under the item's saved name.")
        End If
        If removedSkipped > 0 Then
            notes.Add($"{Fmt.Plural(removedSkipped, "stock record")} of removed items are not included, because their category and pet type are no longer known.")
        End If
        Return (rows, notes)
    End Function

    Private Shared Function MovementLabel(t As StockTransaction) As String
        If t.TxnType = TxnTypes.Adjustment Then Return If(t.QtyChange >= 0, "Adjustment (added)", "Adjustment (deducted)")
        Return t.TypeLabel
    End Function

    Private Shared Function DescriptionText(item As Item) As String
        Return If(item.HasDescription, item.Description.Trim(), "—")
    End Function

    Private Shared Function GroupTable(title As String, groupHeader As String) As ReportTable
        Return New ReportTable With {.Title = title, .Columns = New List(Of ReportColumn) From {
            New ReportColumn(groupHeader, ReportValueKind.Text, 2),
            New ReportColumn("Number of Items", ReportValueKind.Count, 1),
            New ReportColumn("Total Stock Quantity", ReportValueKind.Quantity, 1),
            New ReportColumn("Total Retail Value", ReportValueKind.Money, 1.2)
        }}
    End Function

    Private Shared Function GroupRow(label As String, items As IEnumerable(Of Item)) As Object()
        Return New Object() {label, items.Count(), items.Sum(Function(i) i.Quantity), items.Sum(Function(i) i.RetailValue)}
    End Function

    Private Shared Function DescribeFilters(criteria As ReportCriteria, categories As List(Of LookupItem)) As String
        Dim category = "All categories"
        If criteria.CategoryId.HasValue Then
            category = If(categories.FirstOrDefault(Function(c) c.Id = criteria.CategoryId.Value)?.Name, "(removed category)")
        End If
        Dim petType As String
        Select Case criteria.PetType
            Case Nothing : petType = "All pet types"
            Case PetTypes.Both : petType = "Dog & Cat items only"
            Case Else : petType = $"{criteria.PetType} (includes Dog & Cat items)"
        End Select
        Return $"Category: {category}   ·   Pet type: {petType}"
    End Function
End Class
