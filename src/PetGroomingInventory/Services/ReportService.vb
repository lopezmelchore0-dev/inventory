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
            QuantityStat(current, "Total stock quantity", Amounts(items)),
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
            QuantityStat(period, "Stock received", Amounts(movement.Rows.Where(Function(t) t.TxnType <> TxnTypes.Adjustment))),
            QuantityStat(period, "Added by adjustments", Amounts(adjustments.Where(Function(t) t.QtyChange > 0))),
            QuantityStat(period, "Deducted by adjustments",
                         Amounts(adjustments.Where(Function(t) t.QtyChange < 0)).Select(Function(c) (Qty:=Math.Abs(c.Qty), Unit:=c.Unit)).ToList()),
            New ReportStat(period, "Stock transactions", movement.Rows.Count, ReportValueKind.Count)
        })

        Dim byCategory = GroupTable("Current stock by category", "Category", items)
        For Each group In items.GroupBy(Function(i) i.CategoryName).OrderBy(Function(g) g.Key, StringComparer.CurrentCultureIgnoreCase)
            byCategory.Rows.Add(GroupRow(group.Key, group, items))
        Next
        byCategory.Totals = GroupRow("Total", items, items)

        Dim byPetType = GroupTable("Current stock by pet type", "Pet Type", items)
        AddPetTypeRows(byPetType, items, criteria)
        byPetType.Totals = GroupRow("Total", items, items)

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
            New ReportColumn("Unit", ReportValueKind.Text, 0.6),
            New ReportColumn("Price", ReportValueKind.Money, 0.9),
            New ReportColumn("Retail Value", ReportValueKind.Money, 1),
            New ReportColumn("Supplier", ReportValueKind.Text, 1.3),
            New ReportColumn("Expiration Date", ReportValueKind.Date, 1),
            New ReportColumn("Stock Status", ReportValueKind.Text, 1),
            New ReportColumn("Date Added", ReportValueKind.Date, 1)
        }}
        For Each i In rows
            table.Rows.Add(New Object() {i.Name, i.CategoryName, i.PetTypeText, DescriptionText(i), i.Quantity, UnitText(i.Unit, i.Quantity),
                                         i.RetailPrice, i.RetailValue, i.SupplierName, i.ExpirationDate, i.StatusText, i.CreatedAt.Date})
        Next
        table.Totals = New Object() {"Total", Nothing, Nothing, Nothing, QuantityTotal(Amounts(rows)), UnitTotal(Amounts(rows)), Nothing,
                                     rows.Sum(Function(i) i.RetailValue), Nothing, Nothing, Nothing, Nothing}
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Items added in period", rows.Count, ReportValueKind.Count),
            QuantityStat("", "Total stock quantity", Amounts(rows)),
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
            New ReportColumn("Unit", ReportValueKind.Text, 0.6),
            New ReportColumn("Minimum Stock Level", ReportValueKind.Count, 1.1),
            New ReportColumn("Stock Status", ReportValueKind.Text, 1)
        }}
        For Each i In rows
            table.Rows.Add(New Object() {i.Name, i.CategoryName, i.PetTypeText, DescriptionText(i), i.Quantity, UnitText(i.Unit, i.Quantity),
                                         settings.LowStockThreshold, i.StatusText})
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
            New ReportColumn("Current Quantity", ReportValueKind.Quantity, 1),
            New ReportColumn("Unit", ReportValueKind.Text, 0.6)
        }}
        For Each i In rows
            table.Rows.Add(New Object() {i.Name, i.CategoryName, i.PetTypeText, DescriptionText(i), i.SupplierName, i.Quantity, UnitText(i.Unit, i.Quantity)})
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
            New ReportColumn("Unit", ReportValueKind.Text, 0.6),
            New ReportColumn("Expiration Date", ReportValueKind.Date, 1),
            New ReportColumn("Expiration Status", ReportValueKind.Text, 1.1),
            New ReportColumn("Time Left", ReportValueKind.Text, 1.1)
        }}
        For Each i In rows
            table.Rows.Add(New Object() {i.Name, i.CategoryName, i.PetTypeText, i.Quantity, UnitText(i.Unit, i.Quantity),
                                         i.ExpirationDate, i.ExpirationLabel, i.ExpirationDetail})
        Next
        table.Totals = New Object() {"Total", Nothing, Nothing, QuantityTotal(Amounts(rows)), UnitTotal(Amounts(rows)), Nothing, Nothing, Nothing}
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Expired", rows.Where(Function(i) i.ExpiryStatus = ExpirationStatus.Expired).Count(), ReportValueKind.Count),
            New ReportStat("", $"Expiring soon (within {settings.ExpiringSoonDays} days)", rows.Where(Function(i) i.ExpiryStatus = ExpirationStatus.ExpiringSoon).Count(), ReportValueKind.Count),
            New ReportStat("", "Expiring later", rows.Where(Function(i) i.ExpiryStatus = ExpirationStatus.Ok).Count(), ReportValueKind.Count),
            QuantityStat("", "Quantity affected", Amounts(rows))
        })
        result.IsEmpty = rows.Count = 0
        result.EmptyMessage = "No items expire within the selected date range with the selected filters."
        result.Notes.Add($"Expired = the expiration date has passed. Expiring Soon = expires within {settings.ExpiringSoonDays} days, counting today (set in Settings).")
        result.Notes.Add("Items without an expiration date are not included.")
    End Sub

    Private Shared Sub BuildCategory(result As ReportResult, items As List(Of Item), criteria As ReportCriteria, categories As List(Of LookupItem))
        Dim table = GroupTable(Nothing, "Category", items)
        For Each category In categories.Where(Function(c) Not criteria.CategoryId.HasValue OrElse c.Id = criteria.CategoryId.Value).
                                        OrderBy(Function(c) c.Name, StringComparer.CurrentCultureIgnoreCase)
            table.Rows.Add(GroupRow(category.Name, items.Where(Function(i) i.CategoryId = category.Id), items))
        Next
        table.Totals = GroupRow("Total", items, items)
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Categories with items", items.Select(Function(i) i.CategoryId).Distinct().Count(), ReportValueKind.Count),
            New ReportStat("", "Total items", items.Count, ReportValueKind.Count),
            QuantityStat("", "Total stock quantity", Amounts(items)),
            New ReportStat("", "Total retail value", items.Sum(Function(i) i.RetailValue), ReportValueKind.Money)
        })
        result.IsEmpty = items.Count = 0
        result.EmptyMessage = "No items match the selected category and pet type."
        result.Notes.Add("Categories come from Settings. Categories with no matching items are listed with zero.")
        result.Notes.Add(RetailValueNote)
    End Sub

    Private Shared Sub BuildPetType(result As ReportResult, items As List(Of Item), criteria As ReportCriteria)
        Dim table = GroupTable(Nothing, "Pet Type", items)
        AddPetTypeRows(table, items, criteria)
        table.Totals = GroupRow("Total", items, items)
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Total items", items.Count, ReportValueKind.Count),
            New ReportStat("", "Usable for dogs (Dog + Dog & Cat)", items.Where(Function(i) PetTypes.Matches(i.PetType, PetTypes.Dog)).Count(), ReportValueKind.Count),
            New ReportStat("", "Usable for cats (Cat + Dog & Cat)", items.Where(Function(i) PetTypes.Matches(i.PetType, PetTypes.Cat)).Count(), ReportValueKind.Count),
            New ReportStat("", "Other pet types", items.Where(Function(i) Not PetTypes.IsBuiltIn(i.PetType)).Count(), ReportValueKind.Count),
            New ReportStat("", "Total retail value", items.Sum(Function(i) i.RetailValue), ReportValueKind.Money)
        })
        result.IsEmpty = items.Count = 0
        result.EmptyMessage = "No items match the selected category and pet type."
        result.Notes.Add("Each item is counted once, under its own pet type. ""Dog & Cat"" items are not added again to the Dog or Cat rows, so the totals have no double counting.")
        result.Notes.Add(RetailValueNote)
    End Sub

    ''' <summary>
    ''' One row per pet type: Dog, Cat and Dog &amp; Cat always (zero when unused), then each custom pet type in use.
    ''' Only the rows the pet type filter allows are added.
    ''' </summary>
    Private Shared Sub AddPetTypeRows(table As ReportTable, items As List(Of Item), criteria As ReportCriteria)
        For Each petType In PetTypes.Ordered(items.Select(Function(i) i.PetType)).Where(Function(p) PetTypes.Matches(p, criteria.PetType))
            Dim groupItems = items.Where(Function(i) String.Equals(If(i.PetType, "").Trim(), petType, StringComparison.OrdinalIgnoreCase))
            table.Rows.Add(GroupRow(PetTypes.Label(petType), groupItems, items))
        Next
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
            New ReportColumn("Unit", ReportValueKind.Text, 0.6),
            New ReportColumn("Reason", ReportValueKind.Text, 1.1),
            New ReportColumn("Notes", ReportValueKind.Text, 1.5)
        }}
        For Each t In movement.Rows
            table.Rows.Add(New Object() {t.TxnDate.Date, t.ItemName, MovementLabel(t), t.QtyChange, t.QtyBefore, t.QtyAfter, UnitText(t.Unit, t.QtyAfter),
                                         If(String.IsNullOrWhiteSpace(t.Reason), "—", t.Reason), t.NoteText})
        Next
        Dim changes = Amounts(movement.Rows)
        Dim added = changes.Where(Function(c) c.Qty > 0).ToList()
        Dim deducted = changes.Where(Function(c) c.Qty < 0).Select(Function(c) (Qty:=Math.Abs(c.Qty), Unit:=c.Unit)).ToList()
        table.Totals = New Object() {"Net change", Nothing, Nothing, QuantityTotal(changes, signed:=True), Nothing, Nothing, UnitTotal(changes), Nothing, Nothing}
        result.Tables.Add(table)

        result.Stats.AddRange({
            New ReportStat("", "Transactions", movement.Rows.Count, ReportValueKind.Count),
            QuantityStat("", "Stock received", Amounts(movement.Rows.Where(Function(t) t.TxnType <> TxnTypes.Adjustment))),
            QuantityStat("", "Total added", added),
            QuantityStat("", "Total deducted", deducted),
            QuantityStat("", "Net change", changes, signed:=True)
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

    ''' <summary>
    ''' Totals per group. When every item in the table shares one unit the quantity column stays numeric and the
    ''' header names the unit; otherwise each cell lists the total per unit (see GroupRow).
    ''' </summary>
    Private Shared Function GroupTable(title As String, groupHeader As String, tableItems As IEnumerable(Of Item)) As ReportTable
        Dim unit As String = Nothing
        Dim quantityHeader = "Total Stock Quantity"
        If SharedUnit(Amounts(tableItems), unit) AndAlso unit.Length > 0 Then quantityHeader &= $" ({MeasurementUnits.Display(unit, 2D)})"
        Return New ReportTable With {.Title = title, .Columns = New List(Of ReportColumn) From {
            New ReportColumn(groupHeader, ReportValueKind.Text, 2),
            New ReportColumn("Number of Items", ReportValueKind.Count, 1),
            New ReportColumn(quantityHeader, ReportValueKind.Quantity, 1.3),
            New ReportColumn("Total Retail Value", ReportValueKind.Money, 1.2)
        }}
    End Function

    Private Shared Function GroupRow(label As String, items As IEnumerable(Of Item), tableItems As IEnumerable(Of Item)) As Object()
        Dim quantity As Object
        If SharedUnit(Amounts(tableItems), Nothing) Then
            quantity = items.Sum(Function(i) i.Quantity)
        Else
            quantity = Fmt.QtyByUnit(Amounts(items))
        End If
        Return New Object() {label, items.Count(), quantity, items.Sum(Function(i) i.RetailValue)}
    End Function

    ' ===================== Units =====================
    ' Quantities in different units cannot be added (no conversion), so totals are kept per unit.

    Private Shared Function Amounts(items As IEnumerable(Of Item)) As List(Of (Qty As Decimal, Unit As String))
        Return items.Select(Function(i) (Qty:=i.Quantity, Unit:=i.Unit)).ToList()
    End Function

    Private Shared Function Amounts(transactions As IEnumerable(Of StockTransaction)) As List(Of (Qty As Decimal, Unit As String))
        Return transactions.Select(Function(t) (Qty:=t.QtyChange, Unit:=t.Unit)).ToList()
    End Function

    ''' <summary>True when every entry has the same unit (or none has a unit, or there are no entries).</summary>
    Private Shared Function SharedUnit(entries As IEnumerable(Of (Qty As Decimal, Unit As String)), ByRef unit As String) As Boolean
        Dim units = entries.Select(Function(e) If(e.Unit, "").Trim()).Distinct(StringComparer.Ordinal).ToList()
        unit = If(units.Count = 1, units(0), "")
        Return units.Count <= 1
    End Function

    ''' <summary>A number when all entries share one unit (so Excel keeps a numeric cell), otherwise "120 pcs · 3,500 ml".</summary>
    Private Shared Function QuantityTotal(entries As List(Of (Qty As Decimal, Unit As String)), Optional signed As Boolean = False) As Object
        If SharedUnit(entries, Nothing) Then Return entries.Sum(Function(e) e.Qty)
        Return Fmt.QtyByUnit(entries, signed)
    End Function

    ''' <summary>The Unit cell of a totals row: the shared unit, or blank when the rows mix units.</summary>
    Private Shared Function UnitTotal(entries As List(Of (Qty As Decimal, Unit As String))) As Object
        Dim unit As String = Nothing
        If Not SharedUnit(entries, unit) OrElse unit.Length = 0 Then Return Nothing
        Return MeasurementUnits.Display(unit, entries.Sum(Function(e) e.Qty))
    End Function

    ''' <summary>A quantity summary figure: "Total stock quantity (pcs): 48" for one unit, else "120 pcs · 3,500 ml".</summary>
    Private Shared Function QuantityStat(section As String, label As String, entries As List(Of (Qty As Decimal, Unit As String)),
                                         Optional signed As Boolean = False) As ReportStat
        Dim kind = If(signed, ReportValueKind.SignedQuantity, ReportValueKind.Quantity)
        Dim unit As String = Nothing
        If Not SharedUnit(entries, unit) Then Return New ReportStat(section, label, Fmt.QtyByUnit(entries, signed), kind)
        Dim total = entries.Sum(Function(e) e.Qty)
        Dim suffix = If(unit.Length = 0, "", $" ({MeasurementUnits.Display(unit, total)})")
        Return New ReportStat(section, label & suffix, total, kind)
    End Function

    Private Shared Function UnitText(unit As String, quantity As Decimal) As String
        Return If(String.IsNullOrWhiteSpace(unit), "—", MeasurementUnits.Display(unit, quantity))
    End Function

    Private Shared Function DescribeFilters(criteria As ReportCriteria, categories As List(Of LookupItem)) As String
        Dim category = "All categories"
        If criteria.CategoryId.HasValue Then
            category = If(categories.FirstOrDefault(Function(c) c.Id = criteria.CategoryId.Value)?.Name, "(removed category)")
        End If
        Dim petType As String
        Select Case criteria.PetType
            Case Nothing : petType = "All pet types"
            Case PetTypes.Both : petType = "Dog & Cat items"
            Case PetTypes.Dog, PetTypes.Cat : petType = $"{criteria.PetType} (includes Dog & Cat items)"
            Case Else : petType = criteria.PetType
        End Select
        Return $"Category: {category}   ·   Pet type: {petType}"
    End Function
End Class
