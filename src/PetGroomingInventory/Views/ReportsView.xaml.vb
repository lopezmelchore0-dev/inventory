Imports Microsoft.Win32

Class ReportsView

    Private ReadOnly _errors As FormErrors
    Private _updating As Boolean
    Private _busy As Boolean
    ''' <summary>The report on screen; print and export always use this one.</summary>
    Private _report As ReportResult

    Public Sub New()
        InitializeComponent()
        _errors = New FormErrors(GeneralError)
        _errors.Register("from", FromError, FromPicker)
        _errors.Register("to", ToError, ToPicker)

        _updating = True
        TypeBox.ItemsSource = ReportTypes.All.Select(Function(t) New FilterOption(ReportTypes.Title(t), t)).ToList()
        PetTypeBox.ItemsSource = New List(Of FilterOption) From {
            New FilterOption("All pet types", Nothing),
            New FilterOption("Dog", PetTypes.Dog),
            New FilterOption("Cat", PetTypes.Cat),
            New FilterOption("Dog & Cat only", PetTypes.Both)
        }
        CategoryBox.ItemsSource = New List(Of FilterOption) From {New FilterOption("All categories", Nothing)}
        _updating = False
        ResetFilters()
    End Sub

    Protected Overrides Sub LoadData()
        RefreshCategories()
        ' Keep the report on screen in step with the data, e.g. after a Stock In elsewhere.
        If _report IsNot Nothing AndAlso Not _busy Then ShowReport(ReportService.Generate(_report.Criteria))
    End Sub

    Private Sub RefreshCategories()
        _updating = True
        Try
            Dim previous = SelectedValue(CategoryBox)
            Dim options As New List(Of FilterOption) From {New FilterOption("All categories", Nothing)}
            options.AddRange(LookupService.GetAll(LookupKind.Category).Select(Function(c) New FilterOption(c.Name, c.Id)))
            CategoryBox.ItemsSource = options
            CategoryBox.SelectedItem = If(options.FirstOrDefault(Function(o) Object.Equals(o.Value, previous)), options(0))
        Finally
            _updating = False
        End Try
    End Sub

    ' ===================== Filters =====================

    Private Shared Function SelectedValue(combo As ComboBox) As Object
        Return TryCast(combo.SelectedItem, FilterOption)?.Value
    End Function

    Private Function SelectedType() As ReportType
        Dim value = SelectedValue(TypeBox)
        Return If(value Is Nothing, ReportType.Summary, DirectCast(value, ReportType))
    End Function

    Private Function ReadCriteria() As ReportCriteria
        Return New ReportCriteria With {
            .Type = SelectedType(),
            .FromDate = FromPicker.SelectedDate,
            .ToDate = ToPicker.SelectedDate,
            .CategoryId = CType(SelectedValue(CategoryBox), Integer?),
            .PetType = TryCast(SelectedValue(PetTypeBox), String)
        }
    End Function

    Private Sub ResetFilters()
        _updating = True
        TypeBox.SelectedIndex = 0
        CategoryBox.SelectedIndex = 0
        PetTypeBox.SelectedIndex = 0
        Dim today = Date.Today
        SetRange(New Date(today.Year, today.Month, 1), New Date(today.Year, today.Month, 1).AddMonths(1).AddDays(-1))
        _updating = False
        UpdateDateFields()
    End Sub

    Private Sub SetRange(fromDate As Date, toDate As Date)
        Dim wasUpdating = _updating
        _updating = True
        FromPicker.SelectedDate = fromDate
        ToPicker.SelectedDate = toDate
        _updating = wasUpdating
        _errors.Clear()
    End Sub

    ''' <summary>Turns the date fields off for reports that show current stock only, and explains which date is used.</summary>
    Private Sub UpdateDateFields()
        Dim type = SelectedType()
        Dim usesDates = ReportTypes.UsesDateRange(type)
        For Each element In New UIElement() {FromPicker, ToPicker, QuickRangePanel}
            element.IsEnabled = usesDates
            element.Opacity = If(usesDates, 1, 0.5)
        Next
        DateHintText.Text = ReportTypes.DateBasis(type)
        _errors.Clear()
    End Sub

    Private Sub Type_Changed(sender As Object, e As SelectionChangedEventArgs)
        If _updating Then Return
        UpdateDateFields()
    End Sub

    ''' <summary>Points out a reversed range as soon as it is picked; missing dates are reported on Generate.</summary>
    Private Sub Date_Changed(sender As Object, e As SelectionChangedEventArgs)
        If _updating Then Return
        _errors.Clear()
        If Not FromPicker.SelectedDate.HasValue OrElse Not ToPicker.SelectedDate.HasValue Then Return
        Dim errors = ReportService.Validate(ReadCriteria())
        If errors.ContainsKey("to") Then
            ToError.Text = errors("to")
            ToError.Visibility = Visibility.Visible
        End If
    End Sub

    Private Sub Today_Click(sender As Object, e As RoutedEventArgs)
        SetRange(Date.Today, Date.Today)
    End Sub

    Private Sub ThisMonth_Click(sender As Object, e As RoutedEventArgs)
        Dim first = New Date(Date.Today.Year, Date.Today.Month, 1)
        SetRange(first, first.AddMonths(1).AddDays(-1))
    End Sub

    Private Sub LastMonth_Click(sender As Object, e As RoutedEventArgs)
        Dim first = New Date(Date.Today.Year, Date.Today.Month, 1).AddMonths(-1)
        SetRange(first, first.AddMonths(1).AddDays(-1))
    End Sub

    Private Sub ThisYear_Click(sender As Object, e As RoutedEventArgs)
        SetRange(New Date(Date.Today.Year, 1, 1), New Date(Date.Today.Year, 12, 31))
    End Sub

    Private Sub AllTime_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim bounds = ReportService.GetDateBounds()
            Dim earliest = If(bounds.Earliest, Date.Today)
            Dim latest = If(bounds.Latest, Date.Today)
            SetRange(If(earliest < Date.Today, earliest, Date.Today), If(latest > Date.Today, latest, Date.Today))
        Catch ex As Exception
            ShowFailure("Couldn't read the date range", ex)
        End Try
    End Sub

    Private Sub Reset_Click(sender As Object, e As RoutedEventArgs)
        ResetFilters()
        ClearReport()
    End Sub

    ' ===================== Generate =====================

    Private Async Sub Generate_Click(sender As Object, e As RoutedEventArgs)
        Dim criteria = ReadCriteria()
        _errors.Clear()
        Dim errors = ReportService.Validate(criteria)
        If errors.Count > 0 Then
            _errors.Show(errors)
            Return
        End If

        SetBusy(True)
        Try
            Dim report = Await Task.Run(Function() ReportService.Generate(criteria))
            ShowReport(report)
            ReportScroll.ScrollToTop()
        Catch ex As InputValidationException
            _errors.Show(ex.Errors)
        Catch ex As Exception
            ShowFailure("Couldn't generate the report", ex)
        Finally
            SetBusy(False)
        End Try
    End Sub

    Private Sub SetBusy(busy As Boolean)
        _busy = busy
        BusyPanel.Visibility = If(busy, Visibility.Visible, Visibility.Collapsed)
        GenerateButton.IsEnabled = Not busy
        ResetButton.IsEnabled = Not busy
        UpdateOutputButtons()
    End Sub

    Private Sub UpdateOutputButtons()
        Dim ready = _report IsNot Nothing AndAlso Not _busy
        PrintButton.IsEnabled = ready
        PdfButton.IsEnabled = ready
        ExcelButton.IsEnabled = ready
    End Sub

    Private Sub ClearReport()
        _report = Nothing
        ReportScroll.Visibility = Visibility.Collapsed
        PlaceholderPanel.Visibility = Visibility.Visible
        StatsPanel.Children.Clear()
        TablesPanel.Children.Clear()
        NotesPanel.Children.Clear()
        UpdateOutputButtons()
    End Sub

    Private Sub ShowReport(report As ReportResult)
        _report = report
        PlaceholderPanel.Visibility = Visibility.Collapsed
        ReportScroll.Visibility = Visibility.Visible

        ReportTitleText.Text = report.Title
        PeriodText.Text = If(ReportTypes.UsesDateRange(report.Criteria.Type), "Reporting period: ", "") & report.PeriodText
        FilterText.Text = report.FilterText
        DateBasisText.Text = report.DateBasisNote
        GeneratedText.Text = "Generated " & report.GeneratedText

        StatsPanel.Children.Clear()
        For Each section In report.Stats.GroupBy(Function(s) s.Section)
            If section.Key <> "" Then
                StatsPanel.Children.Add(New TextBlock With {.Text = section.Key, .FontSize = 14, .FontWeight = FontWeights.SemiBold, .Margin = New Thickness(0, 4, 0, 8)})
            End If
            Dim cards As New WrapPanel()
            For Each stat In section
                cards.Children.Add(StatCard(stat))
            Next
            StatsPanel.Children.Add(cards)
        Next

        TablesPanel.Children.Clear()
        EmptyText.Text = report.EmptyMessage
        EmptyText.Visibility = If(report.IsEmpty, Visibility.Visible, Visibility.Collapsed)
        If Not report.IsEmpty Then
            For Each table In report.Tables
                If Not String.IsNullOrEmpty(table.Title) Then
                    TablesPanel.Children.Add(New TextBlock With {.Text = table.Title, .Style = CType(FindResource("CardTitleText"), Style), .Margin = New Thickness(0, 10, 0, 4)})
                End If
                TablesPanel.Children.Add(BuildGrid(table))
            Next
        End If

        NotesPanel.Children.Clear()
        For Each note In report.Notes
            NotesPanel.Children.Add(New TextBlock With {.Text = "• " & note, .Style = CType(FindResource("MutedText"), Style), .Margin = New Thickness(0, 2, 0, 2)})
        Next
        UpdateOutputButtons()
    End Sub

    Private Function StatCard(stat As ReportStat) As Border
        Dim content As New StackPanel()
        content.Children.Add(New TextBlock With {.Text = stat.Label, .FontSize = 12, .Foreground = StatusPalette.Find("TextSecondaryBrush")})
        content.Children.Add(New TextBlock With {.Text = stat.ValueText, .FontSize = 20, .FontWeight = FontWeights.SemiBold, .Margin = New Thickness(0, 2, 0, 0)})
        Return New Border With {.Style = CType(FindResource("StatCard"), Style), .Child = content}
    End Function

    ''' <summary>A read-only grid for one report table; the totals row (if any) is the last row, in bold.</summary>
    Private Function BuildGrid(table As ReportTable) As DataGrid
        Dim rows = table.Rows.Select(Function(r) table.FormatRow(r)).ToList()
        Dim totals = If(table.Totals Is Nothing, Nothing, table.FormatTotals())
        If totals IsNot Nothing Then rows.Add(totals)

        Dim grid As New DataGrid With {
            .ItemsSource = rows, .CanUserSortColumns = False, .Margin = New Thickness(0, 0, 0, 16),
            .VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        }
        Dim rowStyle As New Style(GetType(DataGridRow), CType(FindResource(GetType(DataGridRow)), Style))
        rowStyle.Setters.Add(New Setter(DataGridRow.MinHeightProperty, 40.0))
        grid.RowStyle = rowStyle

        Dim cellText = CType(FindResource("CellText"), Style)
        For i = 0 To table.Columns.Count - 1
            Dim column = table.Columns(i)
            Dim path = $"[{i}]"
            Dim style As New Style(GetType(TextBlock), cellText)
            If column.IsNumeric Then
                style.Setters.Add(New Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right))
            Else
                style.Setters.Add(New Setter(TextBlock.ToolTipProperty, New Binding(path)))
            End If
            grid.Columns.Add(New DataGridTextColumn With {
                .Header = column.Header, .Binding = New Binding(path), .ElementStyle = style,
                .Width = New DataGridLength(column.Width, DataGridLengthUnitType.Star), .MinWidth = 70
            })
        Next

        AddHandler grid.LoadingRow,
            Sub(s, e)
                ' Rows are recycled, so reset the totals look on every other row.
                If totals IsNot Nothing AndAlso e.Row.Item Is totals Then
                    e.Row.FontWeight = FontWeights.Bold
                    e.Row.Background = StatusPalette.Find("LavenderBrush")
                Else
                    e.Row.ClearValue(DataGridRow.FontWeightProperty)
                    e.Row.ClearValue(DataGridRow.BackgroundProperty)
                End If
            End Sub
        AddHandler grid.PreviewMouseWheel, AddressOf Grid_PreviewMouseWheel
        Return grid
    End Function

    ''' <summary>The grids show every row, so the wheel scrolls the whole report instead.</summary>
    Private Sub Grid_PreviewMouseWheel(sender As Object, e As MouseWheelEventArgs)
        e.Handled = True
        ReportScroll.ScrollToVerticalOffset(ReportScroll.VerticalOffset - e.Delta / 3.0)
    End Sub

    ' ===================== Print and export =====================

    Private Sub Print_Click(sender As Object, e As RoutedEventArgs)
        If _report Is Nothing Then Return
        Try
            If ReportExporter.Print(_report) Then MainWindow.Instance?.ShowToast("The report was sent to the printer.")
        Catch ex As Exception
            Dialogs.ShowError("Couldn't print the report", ex.Message)
        End Try
    End Sub

    Private Sub Pdf_Click(sender As Object, e As RoutedEventArgs)
        Dim report = _report
        If report Is Nothing Then Return
        Dim path = AskSavePath(report, "Export report to PDF", "PDF document (*.pdf)|*.pdf", ".pdf")
        If path IsNot Nothing Then SaveFile(path, Sub() ReportExporter.ExportPdf(report, path))
    End Sub

    Private Sub Excel_Click(sender As Object, e As RoutedEventArgs)
        Dim report = _report
        If report Is Nothing Then Return
        Dim path = AskSavePath(report, "Export report to Excel", "Excel workbook (*.xlsx)|*.xlsx", ".xlsx")
        If path IsNot Nothing Then SaveFile(path, Sub() ReportExporter.ExportExcel(report, path))
    End Sub

    Private Function AskSavePath(report As ReportResult, title As String, filter As String, extension As String) As String
        Dim dialog As New SaveFileDialog With {
            .Title = title,
            .Filter = filter,
            .FileName = report.SuggestedFileName() & extension,
            .DefaultExt = extension,
            .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            .AddExtension = True,
            .OverwritePrompt = True
        }
        If Not dialog.ShowDialog(Window.GetWindow(Me)).GetValueOrDefault() Then Return Nothing
        Return dialog.FileName
    End Function

    Private Shared Sub SaveFile(path As String, save As Action)
        Try
            Mouse.OverrideCursor = Cursors.Wait
            save()
            Mouse.OverrideCursor = Nothing
            MainWindow.Instance?.ShowToast($"Report saved as ""{IO.Path.GetFileName(path)}"".")
        Catch ex As IO.IOException
            Mouse.OverrideCursor = Nothing
            Dialogs.ShowError("Couldn't save the report", $"The file may be open in another program. Close it and try again.{vbLf}{vbLf}{ex.Message}")
        Catch ex As Exception
            Mouse.OverrideCursor = Nothing
            Dialogs.ShowError("Couldn't save the report", ex.Message)
        End Try
    End Sub

    Private Shared Sub ShowFailure(title As String, ex As Exception)
        If TypeOf ex Is NotAuthenticatedException Then
            MainWindow.Instance?.ShowAuth(AuthScreen.Login, ex.Message)
        ElseIf ErrorText.IsConnectionError(ex) Then
            MainWindow.Instance?.ShowDbError(ex)
        Else
            Dialogs.ShowError(title, ErrorText.Describe(ex))
        End If
    End Sub
End Class
