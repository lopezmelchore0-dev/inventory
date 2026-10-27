Class ActivitiesView

    Private Const MaxRows As Integer = 1000
    Private _updating As Boolean

    Public Sub New()
        InitializeComponent()
        _updating = True
        TypeFilter.ItemsSource = New List(Of FilterOption) From {New FilterOption("All activity types", Nothing)}
        TypeFilter.SelectedIndex = 0
        _updating = False
    End Sub

    Protected Overrides Sub LoadData()
        RefreshTypes()
        Search()
    End Sub

    Private Sub RefreshTypes()
        _updating = True
        Try
            Dim previous = TryCast(TryCast(TypeFilter.SelectedItem, FilterOption)?.Value, String)
            Dim options As New List(Of FilterOption) From {New FilterOption("All activity types", Nothing)}
            options.AddRange(ActivityService.GetTypes().Select(Function(t) New FilterOption(t, t)))
            TypeFilter.ItemsSource = options
            TypeFilter.SelectedItem = If(options.FirstOrDefault(Function(o) previous IsNot Nothing AndAlso previous.Equals(o.Value)), options(0))
        Finally
            _updating = False
        End Try
    End Sub

    Private Sub Search()
        If _updating Then Return
        If FromPicker.SelectedDate.HasValue AndAlso ToPicker.SelectedDate.HasValue AndAlso FromPicker.SelectedDate.Value > ToPicker.SelectedDate.Value Then
            ActivityGrid.ItemsSource = Nothing
            ShowEmpty("The From date is after the To date. Adjust the date range.")
            CountText.Text = ""
            Return
        End If

        Dim type = TryCast(TryCast(TypeFilter.SelectedItem, FilterOption)?.Value, String)
        Dim rows = ActivityService.Search(FromPicker.SelectedDate, ToPicker.SelectedDate, type, SearchBox.Text, MaxRows)
        ActivityGrid.ItemsSource = rows
        CountText.Text = If(rows.Count >= MaxRows, $"Showing latest {MaxRows:N0} activities", Fmt.Plural(rows.Count, "activity", "activities"))

        Dim filtered = Not String.IsNullOrWhiteSpace(SearchBox.Text) OrElse type IsNot Nothing OrElse
                       FromPicker.SelectedDate.HasValue OrElse ToPicker.SelectedDate.HasValue
        If rows.Count = 0 Then
            ShowEmpty(If(filtered, "No activities match your filters.", "No activity recorded yet. Inventory operations will appear here."))
        Else
            EmptyText.Visibility = Visibility.Collapsed
            ActivityGrid.Visibility = Visibility.Visible
        End If
    End Sub

    Private Sub ShowEmpty(message As String)
        EmptyText.Text = message
        EmptyText.Visibility = Visibility.Visible
        ActivityGrid.Visibility = Visibility.Collapsed
    End Sub

    Private Sub Filter_Changed(sender As Object, e As RoutedEventArgs)
        If _updating Then Return
        SafeSearch()
    End Sub

    Private Sub SafeSearch()
        Try
            Search()
        Catch ex As Exception
            If ErrorText.IsConnectionError(ex) Then
                MainWindow.Instance?.ShowDbError(ex)
            Else
                Dialogs.ShowError("Couldn't load activities", ErrorText.Describe(ex))
            End If
        End Try
    End Sub

    Private Sub Clear_Click(sender As Object, e As RoutedEventArgs)
        _updating = True
        SearchBox.Text = ""
        TypeFilter.SelectedIndex = 0
        FromPicker.SelectedDate = Nothing
        ToPicker.SelectedDate = Nothing
        _updating = False
        SafeSearch()
    End Sub
End Class
