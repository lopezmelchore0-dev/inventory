Imports System.Globalization
Imports ClosedXML.Excel
Imports PdfSharp.Fonts
Imports MD = MigraDoc.DocumentObjectModel
Imports MDR = MigraDoc.Rendering
Imports MDT = MigraDoc.DocumentObjectModel.Tables

''' <summary>
''' Print, PDF and Excel output for a generated report. Every output is built from the same ReportResult
''' that is on screen, so they always match the selected report type, date range and filters.
''' </summary>
Public NotInheritable Class ReportExporter
    Private Sub New()
    End Sub

    ' Brand colors (Themes/Colors.xaml), repeated here so output does not depend on app resources.
    Private Const AccentHex As String = "#A9437F"
    Private Const TintHex As String = "#FCE4F0"
    Private Const BorderHex As String = "#F3CFE3"
    Private Const TextHex As String = "#2E1B28"
    Private Const MutedHex As String = "#7A5F70"

    ''' <summary>More columns than this print on landscape pages.</summary>
    Private Const PortraitMaxColumns As Integer = 6

    Private Shared Function IsLandscape(report As ReportResult) As Boolean
        Return report.MaxColumns > PortraitMaxColumns
    End Function

    ' ===================== Print =====================

    ''' <summary>Shows the print dialog and prints the report. Returns False when the user cancels.</summary>
    Public Shared Function Print(report As ReportResult) As Boolean
        Dim dialog As New PrintDialog()
        If IsLandscape(report) Then
            Try
                dialog.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape
            Catch ex As Exception
                ' No default printer yet: the user picks one (and the orientation) in the dialog.
            End Try
        End If
        If Not dialog.ShowDialog().GetValueOrDefault() Then Return False

        Dim document = ToFlowDocument(report)
        document.PageWidth = dialog.PrintableAreaWidth
        document.PageHeight = dialog.PrintableAreaHeight
        document.ColumnWidth = Double.PositiveInfinity
        Dim paginator = DirectCast(document, IDocumentPaginatorSource).DocumentPaginator
        dialog.PrintDocument(New FooterPaginator(paginator, $"{report.ShopName} · {report.Title}"), report.Title)
        Return True
    End Function

    ''' <summary>Print layout: report content only, with no navigation, buttons or other app elements.</summary>
    Public Shared Function ToFlowDocument(report As ReportResult) As FlowDocument
        Dim document As New FlowDocument With {
            .FontFamily = New FontFamily("Segoe UI"), .FontSize = 10, .Foreground = WpfBrush(TextHex),
            .PagePadding = New Thickness(44, 40, 44, 56), .ColumnWidth = Double.PositiveInfinity
        }
        document.Blocks.Add(WpfParagraph(report.ShopName.ToUpperInvariant(), 9, WpfBrush(AccentHex), FontWeights.SemiBold))
        document.Blocks.Add(WpfParagraph(report.Title, 19, WpfBrush(TextHex), FontWeights.Bold, 6))
        document.Blocks.Add(WpfParagraph($"Reporting period: {report.PeriodText}", 10, WpfBrush(TextHex), FontWeights.SemiBold))
        document.Blocks.Add(WpfParagraph($"Generated: {report.GeneratedText}", 9.5, WpfBrush(MutedHex), FontWeights.Normal))
        document.Blocks.Add(WpfParagraph(report.FilterText, 9.5, WpfBrush(MutedHex), FontWeights.Normal))
        document.Blocks.Add(WpfParagraph(report.DateBasisNote, 9, WpfBrush(MutedHex), FontWeights.Normal, 12))

        For Each section In report.Stats.GroupBy(Function(s) s.Section)
            If section.Key <> "" Then document.Blocks.Add(WpfParagraph(section.Key, 10.5, WpfBrush(AccentHex), FontWeights.SemiBold, 4))
            document.Blocks.Add(WpfStatsTable(section.ToList()))
        Next

        If report.IsEmpty Then
            document.Blocks.Add(New Paragraph(New Italic(New Run(report.EmptyMessage))) With {.Margin = New Thickness(0, 14, 0, 14)})
        Else
            Dim fontSize = If(report.MaxColumns > 8, 8.5, 9.5)
            For Each table In report.Tables
                If Not String.IsNullOrEmpty(table.Title) Then document.Blocks.Add(WpfParagraph(table.Title, 11, WpfBrush(TextHex), FontWeights.SemiBold, 4))
                document.Blocks.Add(WpfTable(table, fontSize))
            Next
        End If

        If report.Notes.Count > 0 Then
            document.Blocks.Add(WpfParagraph("Notes", 10, WpfBrush(TextHex), FontWeights.SemiBold, 2))
            For Each note In report.Notes
                document.Blocks.Add(WpfParagraph("• " & note, 9, WpfBrush(MutedHex), FontWeights.Normal, 2))
            Next
        End If
        Return document
    End Function

    Private Shared Function WpfTable(table As ReportTable, fontSize As Double) As Table
        Dim result As New Table With {.CellSpacing = 0, .FontSize = fontSize, .Margin = New Thickness(0, 0, 0, 14)}
        For Each column In table.Columns
            result.Columns.Add(New TableColumn With {.Width = New GridLength(column.Width, GridUnitType.Star)})
        Next

        Dim header As New TableRowGroup With {.Background = WpfBrush(TintHex), .Foreground = WpfBrush(AccentHex)}
        header.Rows.Add(WpfRow(table, table.Columns.Select(Function(c) c.Header).ToArray(), FontWeights.SemiBold, True))
        result.RowGroups.Add(header)

        Dim body As New TableRowGroup()
        For Each row In table.Rows
            body.Rows.Add(WpfRow(table, table.FormatRow(row), FontWeights.Normal, False))
        Next
        If table.Totals IsNot Nothing Then
            Dim totals = WpfRow(table, table.FormatTotals(), FontWeights.Bold, False)
            totals.Background = WpfBrush(TintHex)
            body.Rows.Add(totals)
        End If
        result.RowGroups.Add(body)
        Return result
    End Function

    Private Shared Function WpfRow(table As ReportTable, values As String(), weight As FontWeight, isHeader As Boolean) As TableRow
        Dim row As New TableRow With {.FontWeight = weight}
        For i = 0 To values.Length - 1
            Dim alignment = If(table.Columns(i).IsNumeric, TextAlignment.Right, TextAlignment.Left)
            row.Cells.Add(New TableCell(New Paragraph(New Run(values(i))) With {.TextAlignment = alignment, .Margin = New Thickness(0)}) With {
                .Padding = New Thickness(5, 4, 5, 4), .BorderBrush = WpfBrush(BorderHex), .BorderThickness = New Thickness(0, 0, 0, If(isHeader, 1, 0.5))
            })
        Next
        Return row
    End Function

    ''' <summary>Headline figures, four per row: small label above a bold value.</summary>
    Private Shared Function WpfStatsTable(stats As List(Of ReportStat)) As Table
        Const perRow = 4
        Dim result As New Table With {.CellSpacing = 4, .Margin = New Thickness(0, 0, 0, 10)}
        For i = 1 To perRow
            result.Columns.Add(New TableColumn With {.Width = New GridLength(1, GridUnitType.Star)})
        Next
        Dim group As New TableRowGroup()
        For start = 0 To stats.Count - 1 Step perRow
            Dim row As New TableRow()
            For Each stat In stats.Skip(start).Take(perRow)
                Dim paragraph As New Paragraph With {.Margin = New Thickness(0)}
                paragraph.Inlines.Add(New Run(stat.Label) With {.FontSize = 8.5, .Foreground = WpfBrush(MutedHex)})
                paragraph.Inlines.Add(New LineBreak())
                paragraph.Inlines.Add(New Run(stat.ValueText) With {.FontSize = 13, .FontWeight = FontWeights.Bold})
                row.Cells.Add(New TableCell(paragraph) With {
                    .Padding = New Thickness(8, 5, 8, 5), .BorderBrush = WpfBrush(BorderHex), .BorderThickness = New Thickness(0.75)
                })
            Next
            group.Rows.Add(row)
        Next
        result.RowGroups.Add(group)
        Return result
    End Function

    Private Shared Function WpfParagraph(text As String, size As Double, brush As Brush, weight As FontWeight, Optional bottom As Double = 2) As Paragraph
        Return New Paragraph(New Run(text)) With {.FontSize = size, .Foreground = brush, .FontWeight = weight, .Margin = New Thickness(0, 0, 0, bottom)}
    End Function

    Private Shared Function WpfBrush(hex As String) As SolidColorBrush
        Dim brush As New SolidColorBrush(CType(ColorConverter.ConvertFromString(hex), Color))
        brush.Freeze()
        Return brush
    End Function

    ''' <summary>Adds "Shop · Report · Page n of N" to the bottom of every printed page.</summary>
    Private NotInheritable Class FooterPaginator
        Inherits DocumentPaginator

        Private ReadOnly _inner As DocumentPaginator
        Private ReadOnly _footer As String

        Public Sub New(inner As DocumentPaginator, footer As String)
            _inner = inner
            _footer = footer
            _inner.ComputePageCount()
        End Sub

        Public Overrides Function GetPage(pageNumber As Integer) As DocumentPage
            Dim page = _inner.GetPage(pageNumber)
            Dim text As New FormattedText($"{_footer}   ·   Page {pageNumber + 1} of {_inner.PageCount}", CultureInfo.CurrentCulture,
                                          FlowDirection.LeftToRight, New Typeface("Segoe UI"), 8.5, WpfBrush(MutedHex), 1.0)
            Dim footer As New DrawingVisual()
            Using dc = footer.RenderOpen()
                dc.DrawText(text, New Point(44, page.Size.Height - 36))
            End Using
            Dim visual As New ContainerVisual()
            visual.Children.Add(page.Visual)
            visual.Children.Add(footer)
            Return New DocumentPage(visual, page.Size, page.BleedBox, page.ContentBox)
        End Function

        Public Overrides ReadOnly Property IsPageCountValid As Boolean
            Get
                Return _inner.IsPageCountValid
            End Get
        End Property

        Public Overrides ReadOnly Property PageCount As Integer
            Get
                Return _inner.PageCount
            End Get
        End Property

        Public Overrides Property PageSize As Size
            Get
                Return _inner.PageSize
            End Get
            Set(value As Size)
                _inner.PageSize = value
            End Set
        End Property

        Public Overrides ReadOnly Property Source As IDocumentPaginatorSource
            Get
                Return _inner.Source
            End Get
        End Property
    End Class

    ' ===================== PDF =====================

    Private Const PdfFont As String = "Segoe UI"
    Private Shared ReadOnly FontLock As New Object()

    Public Shared Sub ExportPdf(report As ReportResult, path As String)
        EnsurePdfFonts()
        Dim landscape = IsLandscape(report)
        Dim document As New MD.Document()
        document.Info.Title = report.Title
        document.Info.Author = report.ShopName
        Dim normal = document.Styles(MD.StyleNames.Normal)
        normal.Font.Name = PdfFont
        normal.Font.Size = 9
        normal.Font.Color = PdfColor(TextHex)

        Dim section = document.AddSection()
        With section.PageSetup
            ' A4, given as the actual page size (MigraDoc ignores Orientation once the size is set).
            .PageWidth = MD.Unit.FromCentimeter(If(landscape, 29.7, 21))
            .PageHeight = MD.Unit.FromCentimeter(If(landscape, 21, 29.7))
            .LeftMargin = MD.Unit.FromCentimeter(1.5)
            .RightMargin = MD.Unit.FromCentimeter(1.5)
            .TopMargin = MD.Unit.FromCentimeter(1.5)
            .BottomMargin = MD.Unit.FromCentimeter(1.8)
            .FooterDistance = MD.Unit.FromCentimeter(0.8)
        End With
        Dim usableWidth = If(landscape, 29.7, 21) - 3

        Dim footer = section.Footers.Primary.AddParagraph()
        footer.Format.Font.Size = 7.5
        footer.Format.Font.Color = PdfColor(MutedHex)
        footer.AddText($"{report.ShopName} · {report.Title} · Generated {report.GeneratedText} · Page ")
        footer.AddPageField()
        footer.AddText(" of ")
        footer.AddNumPagesField()

        PdfParagraph(section, report.ShopName.ToUpperInvariant(), 8.5, AccentHex, True, 1)
        PdfParagraph(section, report.Title, 18, TextHex, True, 5)
        PdfParagraph(section, $"Reporting period: {report.PeriodText}", 9.5, TextHex, True, 1)
        PdfParagraph(section, $"Generated: {report.GeneratedText}", 8.5, MutedHex, False, 1)
        PdfParagraph(section, report.FilterText, 8.5, MutedHex, False, 1)
        PdfParagraph(section, report.DateBasisNote, 8, MutedHex, False, 10)

        For Each group In report.Stats.GroupBy(Function(s) s.Section)
            If group.Key <> "" Then PdfParagraph(section, group.Key, 10, AccentHex, True, 3)
            PdfStatsTable(section, group.ToList(), usableWidth)
        Next

        If report.IsEmpty Then
            Dim empty = PdfParagraph(section, report.EmptyMessage, 10, TextHex, False, 10)
            empty.Format.Font.Italic = True
            empty.Format.SpaceBefore = MD.Unit.FromPoint(8)
        Else
            Dim fontSize = If(report.MaxColumns > 8, 7.5, 8.5)
            For Each table In report.Tables
                If Not String.IsNullOrEmpty(table.Title) Then PdfParagraph(section, table.Title, 10.5, TextHex, True, 3).Format.KeepWithNext = True
                PdfTable(section, table, usableWidth, fontSize)
                section.AddParagraph().Format.SpaceAfter = MD.Unit.FromPoint(6)
            Next
        End If

        If report.Notes.Count > 0 Then
            PdfParagraph(section, "Notes", 9.5, TextHex, True, 2).Format.KeepWithNext = True
            For Each note In report.Notes
                PdfParagraph(section, "• " & note, 8, MutedHex, False, 2)
            Next
        End If

        Dim renderer As New MDR.PdfDocumentRenderer() With {.Document = document}
        renderer.RenderDocument()
        renderer.PdfDocument.Save(path)
    End Sub

    Private Shared Sub PdfTable(section As MD.Section, table As ReportTable, usableWidth As Double, fontSize As Double)
        Dim pdf = section.AddTable()
        pdf.Format.Font.Size = fontSize
        pdf.Borders.Color = PdfColor(BorderHex)
        pdf.Borders.Width = 0.5
        pdf.TopPadding = MD.Unit.FromPoint(2.5)
        pdf.BottomPadding = MD.Unit.FromPoint(2.5)
        pdf.LeftPadding = MD.Unit.FromPoint(3)
        pdf.RightPadding = MD.Unit.FromPoint(3)

        Dim totalWeight = table.Columns.Sum(Function(c) c.Width)
        For Each column In table.Columns
            Dim pdfColumn = pdf.AddColumn(MD.Unit.FromCentimeter(usableWidth * column.Width / totalWeight))
            pdfColumn.Format.Alignment = If(column.IsNumeric, MD.ParagraphAlignment.Right, MD.ParagraphAlignment.Left)
        Next

        Dim header = pdf.AddRow()
        header.HeadingFormat = True
        header.Shading.Color = PdfColor(TintHex)
        header.Format.Font.Bold = True
        header.Format.Font.Color = PdfColor(AccentHex)
        For i = 0 To table.Columns.Count - 1
            header.Cells(i).AddParagraph(table.Columns(i).Header)
        Next

        For Each values In table.Rows
            AddPdfRow(pdf, table.FormatRow(values))
        Next
        If table.Totals IsNot Nothing Then
            Dim totals = AddPdfRow(pdf, table.FormatTotals())
            totals.Format.Font.Bold = True
            totals.Shading.Color = PdfColor(TintHex)
        End If
    End Sub

    Private Shared Function AddPdfRow(pdf As MDT.Table, values As String()) As MDT.Row
        Dim row = pdf.AddRow()
        For i = 0 To values.Length - 1
            row.Cells(i).AddParagraph(values(i))
        Next
        Return row
    End Function

    Private Shared Sub PdfStatsTable(section As MD.Section, stats As List(Of ReportStat), usableWidth As Double)
        Const perRow = 4
        Dim pdf = section.AddTable()
        pdf.Borders.Color = PdfColor(BorderHex)
        pdf.Borders.Width = 0.5
        pdf.TopPadding = MD.Unit.FromPoint(3)
        pdf.BottomPadding = MD.Unit.FromPoint(3)
        pdf.LeftPadding = MD.Unit.FromPoint(5)
        For i = 1 To perRow
            pdf.AddColumn(MD.Unit.FromCentimeter(usableWidth / perRow))
        Next
        For start = 0 To stats.Count - 1 Step perRow
            Dim row = pdf.AddRow()
            Dim cell = 0
            For Each stat In stats.Skip(start).Take(perRow)
                Dim label = row.Cells(cell).AddParagraph(stat.Label)
                label.Format.Font.Size = 7.5
                label.Format.Font.Color = PdfColor(MutedHex)
                Dim value = row.Cells(cell).AddParagraph(stat.ValueText)
                value.Format.Font.Size = 11.5
                value.Format.Font.Bold = True
                cell += 1
            Next
        Next
        section.AddParagraph().Format.SpaceAfter = MD.Unit.FromPoint(4)
    End Sub

    Private Shared Function PdfParagraph(section As MD.Section, text As String, size As Double, hex As String, bold As Boolean, spaceAfter As Double) As MD.Paragraph
        Dim paragraph = section.AddParagraph(text)
        paragraph.Format.Font.Size = size
        paragraph.Format.Font.Bold = bold
        paragraph.Format.Font.Color = PdfColor(hex)
        paragraph.Format.SpaceAfter = MD.Unit.FromPoint(spaceAfter)
        Return paragraph
    End Function

    Private Shared Function PdfColor(hex As String) As MD.Color
        Dim c = CType(ColorConverter.ConvertFromString(hex), Color)
        Return New MD.Color(c.R, c.G, c.B)
    End Function

    ''' <summary>PDFsharp needs a font source; Segoe UI (which includes the ₱ sign) is read from the Windows Fonts folder.</summary>
    Private Shared Sub EnsurePdfFonts()
        SyncLock FontLock
            If GlobalFontSettings.FontResolver Is Nothing Then GlobalFontSettings.FontResolver = New SegoeFontResolver()
        End SyncLock
    End Sub

    Private NotInheritable Class SegoeFontResolver
        Implements IFontResolver

        Public Function ResolveTypeface(familyName As String, bold As Boolean, italic As Boolean) As FontResolverInfo Implements IFontResolver.ResolveTypeface
            Dim face = If(bold, If(italic, "segoeuiz", "segoeuib"), If(italic, "segoeuii", "segoeui"))
            Return New FontResolverInfo(face)
        End Function

        Public Function GetFont(faceName As String) As Byte() Implements IFontResolver.GetFont
            Return IO.File.ReadAllBytes(IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), faceName & ".ttf"))
        End Function
    End Class

    ' ===================== Excel =====================

    Public Shared Sub ExportExcel(report As ReportResult, path As String)
        Using workbook As New XLWorkbook()
            workbook.Properties.Title = report.Title
            workbook.Properties.Author = report.ShopName

            Dim sheet = workbook.Worksheets.Add("Report")
            Dim row = WriteExcelHeader(sheet, report)
            Dim firstHeaderRow = 0
            If report.IsEmpty Then
                sheet.Cell(row, 1).Value = report.EmptyMessage
                sheet.Cell(row, 1).Style.Font.Italic = True
            Else
                For Each table In report.Tables
                    If Not String.IsNullOrEmpty(table.Title) Then
                        sheet.Cell(row, 1).Value = table.Title
                        sheet.Cell(row, 1).Style.Font.Bold = True
                        sheet.Cell(row, 1).Style.Font.FontSize = 12
                        row += 1
                    End If
                    If firstHeaderRow = 0 Then firstHeaderRow = row
                    row = WriteExcelTable(sheet, table, row) + 1
                Next
            End If
            If firstHeaderRow > 0 Then
                sheet.Columns(1, report.MaxColumns).AdjustToContents(firstHeaderRow)
                For Each column In sheet.Columns(1, report.MaxColumns)
                    column.Width = Math.Min(Math.Max(column.Width, 10), 55)
                Next
                If report.Tables.Count = 1 Then sheet.SheetView.FreezeRows(firstHeaderRow)
            End If

            Dim summary = workbook.Worksheets.Add("Summary")
            row = WriteExcelHeader(summary, report)
            summary.Cell(row, 1).Value = "Date basis"
            summary.Cell(row, 1).Style.Font.Bold = True
            summary.Cell(row, 2).Value = report.DateBasisNote
            row += 2
            For Each group In report.Stats.GroupBy(Function(s) s.Section)
                If group.Key <> "" Then
                    summary.Cell(row, 1).Value = group.Key
                    summary.Cell(row, 1).Style.Font.Bold = True
                    summary.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml(AccentHex)
                    row += 1
                End If
                For Each stat In group
                    summary.Cell(row, 1).Value = stat.Label
                    SetExcelValue(summary.Cell(row, 2), stat.Value, stat.Kind, IsWhole(stat.Value))
                    summary.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left
                    row += 1
                Next
                row += 1
            Next
            If report.Notes.Count > 0 Then
                summary.Cell(row, 1).Value = "Notes"
                summary.Cell(row, 1).Style.Font.Bold = True
                row += 1
                For Each note In report.Notes
                    summary.Cell(row, 1).Value = "• " & note
                    row += 1
                Next
            End If
            summary.Column(1).Width = 40
            summary.Column(2).Width = 22

            workbook.SaveAs(path)
        End Using
    End Sub

    ''' <summary>Title, shop, period, generated date and filters. Returns the next free row.</summary>
    Private Shared Function WriteExcelHeader(sheet As IXLWorksheet, report As ReportResult) As Integer
        sheet.Cell(1, 1).Value = report.Title
        sheet.Cell(1, 1).Style.Font.Bold = True
        sheet.Cell(1, 1).Style.Font.FontSize = 16
        sheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml(AccentHex)
        sheet.Cell(2, 1).Value = report.ShopName
        sheet.Cell(3, 1).Value = $"Reporting period: {report.PeriodText}"
        sheet.Cell(4, 1).Value = $"Generated: {report.GeneratedText}"
        sheet.Cell(5, 1).Value = report.FilterText
        sheet.Range(2, 1, 5, 1).Style.Font.FontColor = XLColor.FromHtml(MutedHex)
        Return 7
    End Function

    ''' <summary>Writes the header, rows and totals with typed cells. Returns the next free row.</summary>
    Private Shared Function WriteExcelTable(sheet As IXLWorksheet, table As ReportTable, startRow As Integer) As Integer
        Dim row = startRow
        For i = 0 To table.Columns.Count - 1
            Dim cell = sheet.Cell(row, i + 1)
            cell.Value = table.Columns(i).Header
            cell.Style.Font.Bold = True
            cell.Style.Font.FontColor = XLColor.FromHtml(AccentHex)
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(TintHex)
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin
            cell.Style.Border.BottomBorderColor = XLColor.FromHtml(BorderHex)
            If table.Columns(i).IsNumeric Then cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right
        Next

        Dim allRows As New List(Of Object())(table.Rows)
        If table.Totals IsNot Nothing Then allRows.Add(table.Totals)
        Dim whole = Enumerable.Range(0, table.Columns.Count).
                    Select(Function(i) allRows.All(Function(r) IsWhole(r(i)))).ToArray()

        For Each values In table.Rows
            row += 1
            For i = 0 To table.Columns.Count - 1
                SetExcelValue(sheet.Cell(row, i + 1), values(i), table.Columns(i).Kind, whole(i))
            Next
        Next
        If table.Totals IsNot Nothing Then
            row += 1
            For i = 0 To table.Columns.Count - 1
                Dim cell = sheet.Cell(row, i + 1)
                SetExcelValue(cell, table.Totals(i), table.Columns(i).Kind, whole(i))
                cell.Style.Font.Bold = True
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(TintHex)
                cell.Style.Border.TopBorder = XLBorderStyleValues.Thin
            Next
        End If
        Return row + 1
    End Function

    Private Shared Sub SetExcelValue(cell As IXLCell, value As Object, kind As ReportValueKind, whole As Boolean)
        If value Is Nothing Then Return
        If TypeOf value Is String Then
            cell.Value = DirectCast(value, String)
            Return
        End If
        Dim decimals = If(whole, "", ".00")
        Select Case kind
            Case ReportValueKind.Count
                cell.Value = Convert.ToDouble(value)
                cell.Style.NumberFormat.Format = "#,##0"
            Case ReportValueKind.Quantity
                cell.Value = Convert.ToDouble(value)
                cell.Style.NumberFormat.Format = "#,##0" & decimals
            Case ReportValueKind.SignedQuantity
                cell.Value = Convert.ToDouble(value)
                cell.Style.NumberFormat.Format = $"+#,##0{decimals};-#,##0{decimals};0"
            Case ReportValueKind.Money
                cell.Value = Convert.ToDouble(value)
                cell.Style.NumberFormat.Format = $"""{Fmt.Peso}""#,##0.00"
            Case ReportValueKind.Date
                cell.Value = Convert.ToDateTime(value)
                cell.Style.NumberFormat.Format = "mmm d, yyyy"
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left
            Case Else
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture)
        End Select
    End Sub

    ''' <summary>True for numbers without a fractional part (and for text, dates and blanks, which ignore it).</summary>
    Private Shared Function IsWhole(value As Object) As Boolean
        If TypeOf value Is Decimal Then
            Dim d = DirectCast(value, Decimal)
            Return d = Decimal.Truncate(d)
        End If
        Return True
    End Function
End Class
