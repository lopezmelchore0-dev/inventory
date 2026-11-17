Imports Xunit

''' <summary>Units of measure: the catalogue, display, whole-number rules and per-unit report totals (no database needed).</summary>
Public Class MeasurementUnitTests

    ' ===================== Catalogue =====================

    <Theory>
    <InlineData("Pieces (pcs)", "pcs")>
    <InlineData("pieces (PCS)", "pcs")>
    <InlineData("pcs", "pcs")>
    <InlineData("Per Box", "box")>
    <InlineData("Per Batch", "batch")>
    <InlineData("Per Bundle", "bundle")>
    <InlineData("Grams (g)", "g")>
    <InlineData("Milligrams (mg)", "mg")>
    <InlineData("Kilograms (kg)", "kg")>
    <InlineData("Milliliters (ml)", "ml")>
    <InlineData("Liters (L)", "L")>
    <InlineData("l", "L")>
    <InlineData("Tablets", "tablet")>
    <InlineData("Drops", "drop")>
    <InlineData("  sachet  ", "sachet")>
    <InlineData("Roll", "Roll")>
    Public Sub Normalize_MapsLabelsToStoredValues_AndKeepsCustomUnits(typed As String, expected As String)
        Assert.Equal(expected, MeasurementUnits.Normalize(typed))
    End Sub

    <Theory>
    <InlineData(Nothing)>
    <InlineData("")>
    <InlineData("   ")>
    <InlineData("Other (Custom Unit)")>
    <InlineData("other (custom unit)")>
    Public Sub Normalize_BlankOrOtherMeansNoUnitChosen(typed As String)
        Assert.Null(MeasurementUnits.Normalize(typed))
    End Sub

    <Fact>
    Public Sub Labels_ListEveryRequiredUnit_WithOtherLast()
        Dim labels = MeasurementUnits.Labels.ToList()
        Assert.Equal({"Per Batch", "Per Bundle", "Per Box", "Grams (g)", "Milligrams (mg)", "Kilograms (kg)", "Milliliters (ml)",
                      "Liters (L)", "Pieces (pcs)", "Tablets", "Drops", "Other (Custom Unit)"}, labels)
    End Sub

    <Fact>
    Public Sub LabelFor_ShowsTheDropdownLabel_OrTheCustomText()
        Assert.Equal("Pieces (pcs)", MeasurementUnits.LabelFor("pcs"))
        Assert.Equal("Liters (L)", MeasurementUnits.LabelFor("L"))
        Assert.Equal("sachet", MeasurementUnits.LabelFor("sachet"))
        Assert.Equal("", MeasurementUnits.LabelFor(Nothing))
        For Each u In MeasurementUnits.Predefined
            Assert.Equal(u.Value, MeasurementUnits.Normalize(MeasurementUnits.LabelFor(u.Value)))   ' edit → save keeps the unit
        Next
    End Sub

    <Fact>
    Public Sub UnitSuggestions_ShowEachUsedUnitOnce()
        Dim merged = InventoryService.MergeSuggestions(MeasurementUnits.Labels, {"pcs", "sachet", "Sachet", "ml"}.Select(AddressOf MeasurementUnits.LabelFor))
        Assert.Equal(MeasurementUnits.Labels.Concat({"sachet"}), merged)
        Assert.Equal({"Grams (g)", "Milligrams (mg)", "Kilograms (kg)"}, merged.Where(Function(s) SuggestionBox.Matches(s, "g")).Take(3))
    End Sub

    ' ===================== Display =====================

    <Theory>
    <InlineData(12, "pcs", "12 pcs")>
    <InlineData(1, "pcs", "1 pcs")>
    <InlineData(1, "box", "1 box")>
    <InlineData(3, "box", "3 boxes")>
    <InlineData(2, "batch", "2 batches")>
    <InlineData(0, "bundle", "0 bundles")>
    <InlineData(1, "tablet", "1 tablet")>
    <InlineData(30, "tablet", "30 tablets")>
    <InlineData(5, "drop", "5 drops")>
    <InlineData(2.5, "kg", "2.5 kg")>
    <InlineData(1500, "ml", "1,500 ml")>
    <InlineData(4, "sachet", "4 sachet")>
    <InlineData(7, Nothing, "7")>
    <InlineData(7, "", "7")>
    Public Sub Qty_ShowsTheUnit_AndOldItemsWithoutOneShowTheNumberOnly(quantity As Double, unit As String, expected As String)
        Assert.Equal(expected, Fmt.Qty(CDec(quantity), unit))
    End Sub

    <Fact>
    Public Sub SignedQty_AndItemText_IncludeTheUnit()
        Assert.Equal("+3 pcs", Fmt.SignedQty(3D, "pcs"))
        Assert.Equal("−1 box", Fmt.SignedQty(-1D, "box"))
        Assert.Equal("−2 boxes", Fmt.SignedQty(-2D, "box"))
        Dim item As New Item With {.Quantity = 24D, .Unit = "tablet"}
        Assert.Equal("24 tablets", item.QuantityText)
        Assert.Equal("tablets", item.UnitText)
        Assert.Equal("", New Item With {.Quantity = 5D}.UnitText)
        Dim txn As New StockTransaction With {.QtyChange = 3D, .QtyBefore = 12D, .QtyAfter = 15D, .Unit = "pcs"}
        Assert.Equal("+3 pcs", txn.ChangeText)
        Assert.Equal("12 → 15 pcs", txn.BeforeAfterText)
        Assert.Equal("pcs", txn.UnitText)
        Assert.Equal("boxes", New StockTransaction With {.QtyChange = -2D, .Unit = "box"}.UnitText)
        Assert.Equal("bundle", New StockTransaction With {.QtyChange = 1D, .Unit = "bundle"}.UnitText)
        Assert.Equal("—", New StockTransaction With {.QtyChange = 5D}.UnitText)
    End Sub

    ' ===================== Stock changes use the registered unit =====================

    <Theory>
    <InlineData("bundle", "bundle")>
    <InlineData("bundle", "Per Bundle")>
    <InlineData("box", "per box")>
    <InlineData("ml", "Milliliters (ml)")>
    <InlineData("sachet", "sachet")>
    Public Sub CheckUnit_AcceptsTheItemsRegisteredUnit(registered As String, expected As String)
        Assert.Null(StockService.CheckUnit(registered, expected))
    End Sub

    <Theory>
    <InlineData("box", "bundle")>
    <InlineData("box", Nothing)>
    <InlineData("ml", "L")>
    <InlineData("sachet", "Sachet")>
    Public Sub CheckUnit_RejectsADifferentUnit(registered As String, expected As String)
        Assert.Contains("unit was changed to", StockService.CheckUnit(registered, expected))
    End Sub

    <Theory>
    <InlineData(Nothing)>
    <InlineData("")>
    <InlineData("   ")>
    Public Sub CheckUnit_RejectsItemsWithoutAUnit(registered As String)
        Assert.Contains("has no unit yet", StockService.CheckUnit(registered, registered))
    End Sub

    <Fact>
    Public Sub QtyByUnit_TotalsEachUnitSeparately()
        Dim entries = {(Qty:=100D, Unit:="pcs"), (Qty:=20D, Unit:="pcs"), (Qty:=3500D, Unit:="ml"), (Qty:=4D, Unit:=CStr(Nothing))}
        Assert.Equal("3,500 ml · 120 pcs · 4 (no unit)", Fmt.QtyByUnit(entries))
        Assert.Equal("15 pcs", Fmt.QtyByUnit({(Qty:=10D, Unit:="pcs"), (Qty:=5D, Unit:="pcs")}))
        Assert.Equal("15", Fmt.QtyByUnit({(Qty:=10D, Unit:=CStr(Nothing)), (Qty:=5D, Unit:="")}))
        Assert.Equal("+2 kg · −3 pcs", Fmt.QtyByUnit({(Qty:=2D, Unit:="kg"), (Qty:=-3D, Unit:="pcs")}, signed:=True))
        Assert.Equal("0", Fmt.QtyByUnit(Array.Empty(Of (Qty As Decimal, Unit As String))()))
    End Sub

    ' ===================== Whole-number rule =====================

    <Theory>
    <InlineData(2.5, "pcs", False)>
    <InlineData(2.5, "box", False)>
    <InlineData(0.5, "tablet", False)>
    <InlineData(1.5, "drop", False)>
    <InlineData(1.5, "batch", False)>
    <InlineData(1.5, "bundle", False)>
    <InlineData(3, "pcs", True)>
    <InlineData(2.5, "kg", True)>
    <InlineData(0.25, "L", True)>
    <InlineData(2.5, "sachet", True)>
    <InlineData(2.5, Nothing, True)>
    Public Sub CountedUnits_TakeWholeNumbersOnly(quantity As Double, unit As String, ok As Boolean)
        Assert.Equal(ok, InputValidator.CheckWholeForUnit(CDec(quantity), unit, "Quantity") Is Nothing)
    End Sub

    <Fact>
    Public Sub AddItem_RequiresAUnit_AndAWholeQuantityForCountedUnits()
        Dim item As New Item With {.Name = "Slicker Brush", .CategoryId = 1, .PetType = PetTypes.Dog, .SupplierId = 1, .Quantity = 3D, .RetailPrice = 180D}
        Assert.True(InventoryService.Validate(item).ContainsKey("unit"))
        item.Unit = New String("x"c, MeasurementUnits.MaxLength + 1)
        Assert.True(InventoryService.Validate(item).ContainsKey("unit"))
        item.Unit = "sachet"
        Assert.Empty(InventoryService.Validate(item))

        item.Unit = "pcs"
        item.Quantity = 2.5D
        Assert.Equal("Quantity must be a whole number for pcs (e.g. 3, not 2.5).", InventoryService.Validate(item)("quantity"))
        item.Unit = "kg"
        Assert.Empty(InventoryService.Validate(item))
    End Sub

    <Fact>
    Public Sub EditItem_CannotSwitchFractionalStockToACountedUnit()
        Dim item As New Item With {.Id = 7, .Name = "Kibble", .CategoryId = 1, .PetType = PetTypes.Dog, .SupplierId = 1, .Quantity = 2.5D, .Unit = "pcs", .RetailPrice = 90D}
        Dim errors = InventoryService.Validate(item)
        Assert.False(errors.ContainsKey("quantity"))   ' an existing item's quantity is read-only in the form
        Assert.Contains("Adjust the stock first", errors("unit"))
    End Sub

    <Fact>
    Public Sub Adjustment_UsesTheItemsUnit()
        Assert.True(StockService.ValidateAdjustment(10D, AdjustmentMode.Deduct, 1.5D, "Damaged", Nothing, "pcs").ContainsKey("amount"))
        Assert.True(StockService.ValidateAdjustment(10D, AdjustmentMode.SetCount, 7.5D, "Count correction", Nothing, "box").ContainsKey("amount"))
        Assert.Empty(StockService.ValidateAdjustment(10D, AdjustmentMode.Deduct, 1.5D, "Damaged", Nothing, "kg"))
        Assert.Empty(StockService.ValidateAdjustment(10D, AdjustmentMode.Add, 2D, "Count correction", Nothing, "pcs"))
        Assert.Equal("You can deduct at most 3 boxes (the available stock).",
                     StockService.ValidateAdjustment(3D, AdjustmentMode.Deduct, 5D, "Damaged", Nothing, "box")("amount"))
    End Sub

    ' ===================== Reports =====================

    Private Shared ReadOnly Now As New Date(2026, 10, 27, 14, 30, 0)
    Private Shared ReadOnly Categories As New List(Of LookupItem) From {
        New LookupItem With {.Id = 1, .Name = "Medicine"},
        New LookupItem With {.Id = 2, .Name = "Grooming Supplies"}
    }

    Private Shared Function MakeItem(id As Integer, name As String, categoryId As Integer, quantity As Decimal, unit As String) As Item
        Return New Item With {
            .Id = id, .Name = name, .CategoryId = categoryId, .CategoryName = Categories.First(Function(c) c.Id = categoryId).Name,
            .PetType = PetTypes.Both, .Quantity = quantity, .Unit = unit, .RetailPrice = 10D, .SupplierId = 1, .SupplierName = "Paws Supply",
            .CreatedAt = New Date(2026, 10, 5)
        }
    End Function

    Private Shared Function Run(type As ReportType, items As List(Of Item), Optional transactions As List(Of StockTransaction) = Nothing) As ReportResult
        Dim criteria As New ReportCriteria With {.Type = type, .FromDate = New Date(2026, 10, 1), .ToDate = New Date(2026, 10, 31)}
        Return ReportService.Build(criteria, items, If(transactions, New List(Of StockTransaction)), New AppSettings(), Categories, Now)
    End Function

    Private Shared Function Column(table As ReportTable, header As String) As Integer
        Return table.Columns.FindIndex(Function(c) c.Header = header)
    End Function

    <Fact>
    Public Sub InventoryReport_HasAUnitColumn_AndANumericTotalForASingleUnit()
        Dim report = Run(ReportType.InventoryList, New List(Of Item) From {MakeItem(1, "Gauze", 2, 12D, "pcs"), MakeItem(2, "Cotton Swabs", 2, 1D, "pcs")})
        Dim table = report.Tables.Single()
        Dim unitColumn = Column(table, "Unit")
        Assert.Equal(Column(table, "Quantity") + 1, unitColumn)
        Assert.Equal({"pcs", "pcs"}, table.Rows.Select(Function(r) CStr(r(unitColumn))))
        Assert.Equal(13D, table.Totals(Column(table, "Quantity")))
        Assert.Equal("pcs", table.Totals(unitColumn))
        Assert.Equal(13D, report.Stats.Single(Function(s) s.Label = "Total stock quantity (pcs)").Value)
    End Sub

    <Fact>
    Public Sub Reports_TotalMixedUnitsPerUnit_NeverAddingThemTogether()
        Dim items = New List(Of Item) From {
            MakeItem(1, "Gauze", 2, 12D, "pcs"), MakeItem(2, "Ear Drops", 1, 500D, "ml"), MakeItem(3, "Cotton Swabs", 2, 3D, "pcs"),
            MakeItem(4, "Old Item", 2, 2D, Nothing)
        }
        Dim inventory = Run(ReportType.InventoryList, items)
        Dim table = inventory.Tables.Single()
        Assert.Equal("500 ml · 15 pcs · 2 (no unit)", table.Totals(Column(table, "Quantity")))
        Assert.Null(table.Totals(Column(table, "Unit")))
        Assert.Equal("—", table.Rows.Single(Function(r) CStr(r(0)) = "Old Item")(Column(table, "Unit")))
        Assert.Equal("500 ml · 15 pcs · 2 (no unit)", inventory.Stats.Single(Function(s) s.Label = "Total stock quantity").ValueText)

        Dim byCategory = Run(ReportType.Category, items).Tables.Single()
        Dim quantityColumn = Column(byCategory, "Total Stock Quantity")
        Assert.Equal("500 ml", byCategory.Rows.Single(Function(r) CStr(r(0)) = "Medicine")(quantityColumn))
        Assert.Equal("15 pcs · 2 (no unit)", byCategory.Rows.Single(Function(r) CStr(r(0)) = "Grooming Supplies")(quantityColumn))
    End Sub

    <Fact>
    Public Sub CategoryReport_NamesTheUnitInTheHeader_WhenEveryItemSharesIt()
        Dim table = Run(ReportType.Category, New List(Of Item) From {MakeItem(1, "Ear Drops", 1, 500D, "ml"), MakeItem(2, "Shampoo", 2, 250D, "ml")}).Tables.Single()
        Dim quantityColumn = Column(table, "Total Stock Quantity (ml)")
        Assert.True(quantityColumn >= 0)
        Assert.Equal(750D, table.Totals(quantityColumn))
    End Sub

    <Fact>
    Public Sub MovementReport_ShowsEachTransactionsUnit_AndTotalsPerUnit()
        Dim items = New List(Of Item) From {MakeItem(1, "Gauze", 2, 12D, "pcs"), MakeItem(2, "Ear Drops", 1, 500D, "ml")}
        Dim transactions = New List(Of StockTransaction) From {
            New StockTransaction With {.Id = 1, .ItemId = 1, .ItemName = "Gauze", .TxnType = TxnTypes.StockIn, .QtyChange = 10D, .QtyBefore = 4D, .QtyAfter = 14D,
                                       .Unit = "pcs", .TxnDate = New Date(2026, 10, 3), .CreatedAt = New Date(2026, 10, 3)},
            New StockTransaction With {.Id = 2, .ItemId = 1, .ItemName = "Gauze", .TxnType = TxnTypes.Adjustment, .QtyChange = -2D, .QtyBefore = 14D, .QtyAfter = 12D,
                                       .Unit = "pcs", .Reason = "Damaged", .TxnDate = New Date(2026, 10, 4), .CreatedAt = New Date(2026, 10, 4)},
            New StockTransaction With {.Id = 3, .ItemId = 2, .ItemName = "Ear Drops", .TxnType = TxnTypes.StockIn, .QtyChange = 500D, .QtyBefore = 0D, .QtyAfter = 500D,
                                       .Unit = "ml", .TxnDate = New Date(2026, 10, 5), .CreatedAt = New Date(2026, 10, 5)}
        }
        Dim report = Run(ReportType.Movement, items, transactions)
        Dim table = report.Tables.Single()
        Assert.Equal({"pcs", "pcs", "ml"}, table.Rows.Select(Function(r) CStr(r(Column(table, "Unit")))))
        Assert.Equal("+500 ml · +8 pcs", table.Totals(Column(table, "Qty Added / Deducted")))
        Assert.Equal("500 ml · 10 pcs", report.Stats.Single(Function(s) s.Label = "Stock received").ValueText)
        Assert.Equal(2D, report.Stats.Single(Function(s) s.Label = "Total deducted (pcs)").Value)
    End Sub
End Class
