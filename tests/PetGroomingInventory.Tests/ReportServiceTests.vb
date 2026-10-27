Imports System.Threading
Imports ClosedXML.Excel
Imports PdfSharp.Pdf.IO
Imports Xunit

''' <summary>Report rules and outputs, built from in-memory data (no database needed).</summary>
Public Class ReportServiceTests

    ' Generated on Oct 27, 2026. Defaults: low = 10, critical = 5, expiring soon = 30 days.
    Private Shared ReadOnly Now As New Date(2026, 10, 27, 14, 30, 0)
    Private Shared ReadOnly October As (Start As Date, Finish As Date) = (New Date(2026, 10, 1), New Date(2026, 10, 31))

    Private Shared ReadOnly Categories As New List(Of LookupItem) From {
        New LookupItem With {.Id = 1, .Name = "Medicine"},
        New LookupItem With {.Id = 2, .Name = "Vitamins"},
        New LookupItem With {.Id = 3, .Name = "Grooming Supplies"},
        New LookupItem With {.Id = 4, .Name = "Accessories"}
    }

    Private Shared Function MakeItem(id As Integer, name As String, categoryId As Integer, petType As String, quantity As Decimal,
                                     price As Decimal, added As Date, Optional expiry As Date? = Nothing) As Item
        Return New Item With {
            .Id = id, .Name = name, .CategoryId = categoryId, .CategoryName = Categories.First(Function(c) c.Id = categoryId).Name,
            .PetType = petType, .Quantity = quantity, .RetailPrice = price, .SupplierId = 1, .SupplierName = "Paws Supply",
            .CreatedAt = added, .ExpirationDate = expiry
        }
    End Function

    Private Shared Function Items() As List(Of Item)
        Return New List(Of Item) From {
            MakeItem(1, "Dewormer", 1, PetTypes.Dog, 3D, 100D, New Date(2026, 10, 5, 9, 0, 0), New Date(2026, 10, 20)),     ' critical, expired
            MakeItem(2, "Multivitamin", 2, PetTypes.Both, 8D, 50D, New Date(2026, 9, 15), New Date(2026, 11, 10)),            ' low, expiring soon
            MakeItem(3, "Slicker Brush", 3, PetTypes.Dog, 0D, 200D, New Date(2026, 10, 1)),                                   ' out of stock
            MakeItem(4, "Cat Collar", 4, PetTypes.Cat, 25D, 80D, New Date(2025, 12, 1)),                                      ' in stock
            MakeItem(5, "Ear Drops", 1, PetTypes.Cat, 12D, 150D, New Date(2026, 10, 27, 23, 59, 0), New Date(2027, 6, 1))     ' in stock, expires later
        }
    End Function

    Private Shared Function Transactions() As List(Of StockTransaction)
        Return New List(Of StockTransaction) From {
            Txn(1, 1, "Dewormer", TxnTypes.Initial, 3D, 0D, New Date(2026, 10, 5)),
            Txn(2, 5, "Ear Drops", TxnTypes.StockIn, 12D, 0D, New Date(2026, 10, 27)),
            Txn(3, 2, "Multivitamin", TxnTypes.Adjustment, -2D, 10D, New Date(2026, 10, 10)),
            Txn(4, Nothing, "Old Shampoo", TxnTypes.StockIn, 5D, 0D, New Date(2026, 10, 12)),   ' item was removed
            Txn(5, 4, "Cat Collar", TxnTypes.StockIn, 10D, 15D, New Date(2026, 9, 1))           ' outside October
        }
    End Function

    Private Shared Function Txn(id As Integer, itemId As Integer?, name As String, type As String, change As Decimal, before As Decimal, day As Date) As StockTransaction
        Return New StockTransaction With {
            .Id = id, .ItemId = itemId, .ItemName = name, .TxnType = type, .QtyChange = change, .QtyBefore = before,
            .QtyAfter = before + change, .TxnDate = day, .CreatedAt = day.AddHours(10), .Reason = If(type = TxnTypes.Adjustment, "Damaged", Nothing)
        }
    End Function

    Private Shared Function Run(type As ReportType, Optional fromDate As Date? = Nothing, Optional toDate As Date? = Nothing,
                                Optional categoryId As Integer? = Nothing, Optional petType As String = Nothing,
                                Optional settings As AppSettings = Nothing) As ReportResult
        Dim criteria As New ReportCriteria With {
            .Type = type, .FromDate = If(fromDate, October.Start), .ToDate = If(toDate, October.Finish), .CategoryId = categoryId, .PetType = petType
        }
        Assert.Empty(ReportService.Validate(criteria))
        Return ReportService.Build(criteria, Items(), Transactions(), If(settings, New AppSettings()), Categories, Now)
    End Function

    Private Shared Function Column(table As ReportTable, header As String) As Integer
        Return table.Columns.FindIndex(Function(c) c.Header = header)
    End Function

    Private Shared Function Names(report As ReportResult) As List(Of String)
        Dim table = report.Tables.Single()
        Dim index = Column(table, "Item Name")
        Return table.Rows.Select(Function(r) CStr(r(index))).ToList()
    End Function

    Private Shared Function Stat(report As ReportResult, label As String) As Object
        Return report.Stats.Single(Function(s) s.Label = label).Value
    End Function

    ' ===================== Validation =====================

    <Fact>
    Public Sub Validate_RejectsToBeforeFrom_AndAcceptsASingleDay()
        Dim reversed As New ReportCriteria With {.Type = ReportType.Movement, .FromDate = New Date(2026, 10, 10), .ToDate = New Date(2026, 10, 9)}
        Assert.Equal("Inventory To cannot be earlier than Inventory From.", ReportService.Validate(reversed)("to"))

        Dim singleDay As New ReportCriteria With {.Type = ReportType.Movement, .FromDate = New Date(2026, 10, 10), .ToDate = New Date(2026, 10, 10)}
        Assert.Empty(ReportService.Validate(singleDay))
    End Sub

    <Fact>
    Public Sub Validate_RequiresDates_OnlyForDatedReports()
        Dim missing As New ReportCriteria With {.Type = ReportType.InventoryList}
        Dim errors = ReportService.Validate(missing)
        Assert.True(errors.ContainsKey("from"))
        Assert.True(errors.ContainsKey("to"))

        For Each type In {ReportType.LowStock, ReportType.OutOfStock, ReportType.Category, ReportType.PetType}
            Assert.Empty(ReportService.Validate(New ReportCriteria With {.Type = type}))
        Next
        Assert.True(ReportService.Validate(New ReportCriteria With {.Type = ReportType.Expiring, .FromDate = New Date(1990, 1, 1), .ToDate = Now}).ContainsKey("from"))
    End Sub

    <Fact>
    Public Sub Generate_ThrowsOnInvalidRange_BeforeTouchingTheDatabase()
        Dim criteria As New ReportCriteria With {.Type = ReportType.Summary, .FromDate = New Date(2026, 10, 2), .ToDate = New Date(2026, 10, 1)}
        Dim ex = Assert.Throws(Of InputValidationException)(Function() ReportService.Generate(criteria))
        Assert.True(ex.Errors.ContainsKey("to"))
    End Sub

    ' ===================== Reports =====================

    <Fact>
    Public Sub InventoryList_FiltersByDateAdded_IncludingTheWholeLastDay()
        Dim report = Run(ReportType.InventoryList)
        Assert.Equal({"Dewormer", "Ear Drops", "Slicker Brush"}, Names(report))   ' Ear Drops was added at 23:59 on the last day
        Dim table = report.Tables.Single()
        Assert.Equal(15D, table.Totals(Column(table, "Quantity")))
        Assert.Equal(2100D, table.Totals(Column(table, "Retail Value")))          ' 3 × 100 + 12 × 150
        Assert.Equal(2100D, Stat(report, "Total retail value"))

        Assert.Equal({"Ear Drops"}, Names(Run(ReportType.InventoryList, New Date(2026, 10, 27), New Date(2026, 10, 27))))
    End Sub

    <Fact>
    Public Sub InventoryList_AppliesCategoryAndPetTypeFilters()
        Assert.Equal({"Dewormer", "Ear Drops"}, Names(Run(ReportType.InventoryList, categoryId:=1)))
        Assert.Equal({"Dewormer", "Slicker Brush"}, Names(Run(ReportType.InventoryList, petType:=PetTypes.Dog)))
        ' Dog also includes items for both animals, the same as the Inventory page.
        Assert.Equal({"Dewormer", "Multivitamin", "Slicker Brush"}, Names(Run(ReportType.InventoryList, New Date(2026, 1, 1), New Date(2026, 12, 31), petType:=PetTypes.Dog)))
        Assert.Equal({"Multivitamin"}, Names(Run(ReportType.InventoryList, New Date(2026, 1, 1), New Date(2026, 12, 31), petType:=PetTypes.Both)))
    End Sub

    <Fact>
    Public Sub LowStock_UsesTheConfiguredThresholds_AndLeavesOutZeroStock()
        Dim report = Run(ReportType.LowStock)
        Assert.Equal({"Dewormer", "Multivitamin"}, Names(report))                 ' lowest quantity first
        Dim table = report.Tables.Single()
        Assert.All(table.Rows, Sub(r) Assert.Equal(10, r(Column(table, "Minimum Stock Level"))))
        Assert.Equal({"Critical Stock", "Low Stock"}, table.Rows.Select(Function(r) CStr(r(Column(table, "Stock Status")))))

        Dim raised = Run(ReportType.LowStock, settings:=New AppSettings With {.LowStockThreshold = 15, .CriticalStockThreshold = 5})
        Assert.Equal({"Dewormer", "Multivitamin", "Ear Drops"}, Names(raised))
    End Sub

    <Fact>
    Public Sub OutOfStock_ListsOnlyZeroQuantity_AndShowsEmptyMessageWhenNoneMatch()
        Assert.Equal({"Slicker Brush"}, Names(Run(ReportType.OutOfStock)))

        Dim none = Run(ReportType.OutOfStock, petType:=PetTypes.Cat)
        Assert.True(none.IsEmpty)
        Assert.False(String.IsNullOrWhiteSpace(none.EmptyMessage))
        Assert.Empty(none.Tables.Single().Rows)
    End Sub

    <Fact>
    Public Sub Expiring_SeparatesExpiredFromExpiringSoon()
        Dim report = Run(ReportType.Expiring, New Date(2026, 10, 1), New Date(2026, 11, 30))
        Assert.Equal({"Dewormer", "Multivitamin"}, Names(report))
        Dim table = report.Tables.Single()
        Assert.Equal({"Expired", "Expiring Soon"}, table.Rows.Select(Function(r) CStr(r(Column(table, "Expiration Status")))))
        Assert.Equal(1, Stat(report, "Expired"))
        Assert.Equal(1, Stat(report, "Expiring soon (within 30 days)"))

        Dim wide = Run(ReportType.Expiring, New Date(2026, 1, 1), New Date(2027, 12, 31))
        Assert.Equal({"Dewormer", "Multivitamin", "Ear Drops"}, Names(wide))      ' items without an expiration date never appear
    End Sub

    <Fact>
    Public Sub Category_KeepsMedicineAndVitaminsSeparate_WithCorrectTotals()
        Dim report = Run(ReportType.Category)
        Dim table = report.Tables.Single()
        Dim rows = table.Rows.ToDictionary(Function(r) CStr(r(0)))
        Assert.Equal({"Accessories", "Grooming Supplies", "Medicine", "Vitamins"}, rows.Keys)
        Assert.Equal(2, rows("Medicine")(1))
        Assert.Equal(15D, rows("Medicine")(2))
        Assert.Equal(2100D, rows("Medicine")(3))
        Assert.Equal(1, rows("Vitamins")(1))
        Assert.Equal(400D, rows("Vitamins")(3))
        Assert.Equal(5, table.Totals(1))
        Assert.Equal(48D, table.Totals(2))
        Assert.Equal(4500D, table.Totals(3))                                      ' 300 + 400 + 0 + 2000 + 1800

        Dim one = Run(ReportType.Category, categoryId:=2)
        Assert.Equal({"Vitamins"}, one.Tables.Single().Rows.Select(Function(r) CStr(r(0))))
    End Sub

    <Fact>
    Public Sub PetType_CountsEachItemOnce()
        Dim table = Run(ReportType.PetType).Tables.Single()
        Dim counts = table.Rows.ToDictionary(Function(r) CStr(r(0)), Function(r) CInt(r(1)))
        Assert.Equal(2, counts("Dog"))
        Assert.Equal(2, counts("Cat"))
        Assert.Equal(1, counts("Dog & Cat"))
        Assert.Equal(5, table.Totals(1))
        Assert.Equal(counts.Values.Sum(), table.Totals(1))                       ' no double counting of "Dog & Cat"
        Assert.Equal(table.Rows.Sum(Function(r) CDec(r(2))), table.Totals(2))
    End Sub

    <Fact>
    Public Sub Movement_UsesTransactionDates_AndTotalsAddedAndDeducted()
        Dim report = Run(ReportType.Movement)
        Dim table = report.Tables.Single()
        Assert.Equal({"Dewormer", "Multivitamin", "Old Shampoo", "Ear Drops"}, Names(report))   ' oldest first, September excluded
        Assert.Equal(20D, Stat(report, "Total added"))
        Assert.Equal(2D, Stat(report, "Total deducted"))
        Assert.Equal(18D, Stat(report, "Net change"))
        Assert.Equal(18D, table.Totals(Column(table, "Qty Added / Deducted")))
        Assert.Contains(report.Notes, Function(n) n.Contains("later removed"))

        Dim medicine = Run(ReportType.Movement, categoryId:=1)
        Assert.Equal({"Dewormer", "Ear Drops"}, Names(medicine))
        Assert.Contains(medicine.Notes, Function(n) n.Contains("not included"))
    End Sub

    <Fact>
    Public Sub Summary_SeparatesCurrentStockFromPeriodActivity()
        Dim report = Run(ReportType.Summary)
        Dim current = report.Stats.Where(Function(s) s.Section.StartsWith("Current inventory")).ToList()
        Dim period = report.Stats.Where(Function(s) s.Section.StartsWith("Period activity")).ToList()
        Assert.Equal(5, current.Single(Function(s) s.Label = "Total items").Value)
        Assert.Equal(48D, current.Single(Function(s) s.Label = "Total stock quantity").Value)
        Assert.Equal(4500D, current.Single(Function(s) s.Label = "Total retail value").Value)
        Assert.Equal(1, current.Single(Function(s) s.Label = "Out of stock").Value)
        Assert.Equal(3, period.Single(Function(s) s.Label = "Items added").Value)
        Assert.Equal(20D, period.Single(Function(s) s.Label = "Stock received").Value)
        Assert.Equal(2D, period.Single(Function(s) s.Label = "Deducted by adjustments").Value)
        Assert.Equal(4, period.Single(Function(s) s.Label = "Stock transactions").Value)
        Assert.Equal(2, report.Tables.Count)
    End Sub

    <Fact>
    Public Sub CurrentStockReports_SayTheDateRangeDoesNotApply()
        For Each type In {ReportType.LowStock, ReportType.OutOfStock, ReportType.Category, ReportType.PetType}
            Assert.False(ReportTypes.UsesDateRange(type))
            Assert.StartsWith("Current stock as of", Run(type).PeriodText)
        Next
        Assert.Equal("Oct 1, 2026 – Oct 31, 2026", Run(ReportType.Movement).PeriodText)
    End Sub

    ' ===================== Outputs =====================

    <Fact>
    Public Sub ExportExcel_WritesTypedCellsAndTotals()
        Dim report = Run(ReportType.InventoryList)
        Dim path = TempFile(".xlsx")
        Try
            ReportExporter.ExportExcel(report, path)
            Using workbook As New XLWorkbook(path)
                Dim sheet = workbook.Worksheet("Report")
                Assert.Equal(report.Title, sheet.Cell(1, 1).GetString())
                Dim header = sheet.RowsUsed().First(Function(r) r.Cell(1).GetString() = "Item Name").RowNumber()
                Dim quantityColumn = Column(report.Tables.Single(), "Quantity") + 1
                Assert.Equal(XLDataType.Number, sheet.Cell(header + 1, quantityColumn).DataType)
                Dim totals = header + report.Tables.Single().Rows.Count + 1
                Assert.Equal("Total", sheet.Cell(totals, 1).GetString())
                Assert.Equal(15.0, sheet.Cell(totals, quantityColumn).GetDouble())
                Assert.True(sheet.Cell(totals, 1).Style.Font.Bold)
                Assert.NotNull(workbook.Worksheet("Summary"))
            End Using
        Finally
            IO.File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub ExportPdf_WritesAPdf_LandscapeForWideReports()
        Dim wide = Run(ReportType.InventoryList)
        Dim narrow = Run(ReportType.Category)
        Dim widePath = TempFile(".pdf")
        Dim narrowPath = TempFile(".pdf")
        Try
            ReportExporter.ExportPdf(wide, widePath)
            ReportExporter.ExportPdf(narrow, narrowPath)
            Assert.Equal("%PDF", Text.Encoding.ASCII.GetString(IO.File.ReadAllBytes(widePath), 0, 4))
            Using document = PdfReader.Open(widePath, PdfDocumentOpenMode.Import)
                Assert.True(document.Pages(0).Width.Point > document.Pages(0).Height.Point, "11 columns should use a landscape page")
            End Using
            Using document = PdfReader.Open(narrowPath, PdfDocumentOpenMode.Import)
                Assert.True(document.Pages(0).Width.Point < document.Pages(0).Height.Point, "4 columns fit a portrait page")
            End Using
        Finally
            IO.File.Delete(widePath)
            IO.File.Delete(narrowPath)
        End Try
    End Sub

    <Fact>
    Public Sub ExportPdf_HandlesEmptyReports()
        Dim path = TempFile(".pdf")
        Try
            ReportExporter.ExportPdf(Run(ReportType.OutOfStock, petType:=PetTypes.Cat), path)
            Assert.True(New IO.FileInfo(path).Length > 0)
        Finally
            IO.File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub PrintLayout_ContainsTheReportOnly()
        Dim report = Run(ReportType.Movement)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    Dim document = ReportExporter.ToFlowDocument(report)
                    Dim text = New System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd).Text
                    Assert.Contains(report.Title, text)
                    Assert.Contains(report.PeriodText, text)
                    Assert.Contains("Net change", text)
                    Assert.DoesNotContain("Generate Report", text)
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        thread.Join()
        If failure IsNot Nothing Then Throw New Exception("Print layout failed: " & failure.Message, failure)
    End Sub

    Private Shared Function TempFile(extension As String) As String
        Dim folder = IO.Path.Combine(IO.Path.GetTempPath(), "pawstock-tests")
        IO.Directory.CreateDirectory(folder)
        Return IO.Path.Combine(folder, Guid.NewGuid().ToString("N") & extension)
    End Function
End Class
