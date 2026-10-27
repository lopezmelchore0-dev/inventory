Class SuppliersView

    Private _suppliers As New List(Of Supplier)

    Public Sub New()
        InitializeComponent()
    End Sub

    Protected Overrides Sub LoadData()
        _suppliers = SupplierService.GetAll()
        ApplyFilter()
    End Sub

    Private Sub ApplyFilter()
        Dim text = SearchBox.Text.Trim()
        Dim result = If(text.Length = 0, _suppliers,
            _suppliers.Where(Function(s) Matches(s.Name, text) OrElse Matches(s.ContactPerson, text) OrElse
                                         Matches(s.Phone, text) OrElse Matches(s.Address, text) OrElse Matches(s.Email, text)).ToList())
        SuppliersGrid.ItemsSource = result
        CountText.Text = If(text.Length = 0, Fmt.Plural(_suppliers.Count, "supplier"),
                                             $"Showing {result.Count} of {Fmt.Plural(_suppliers.Count, "supplier")}")

        If _suppliers.Count = 0 Then
            ShowEmpty("No suppliers yet", "Add the suppliers you buy products from. Items in inventory are linked to a supplier.", "Add your first supplier")
        ElseIf result.Count = 0 Then
            ShowEmpty("No suppliers match your search", "Try a different name, phone number or address.", "Clear search")
        Else
            EmptyState.Visibility = Visibility.Collapsed
            SuppliersGrid.Visibility = Visibility.Visible
        End If
    End Sub

    Private Shared Function Matches(value As String, text As String) As Boolean
        Return value IsNot Nothing AndAlso value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Private Sub ShowEmpty(title As String, message As String, actionText As String)
        EmptyTitle.Text = title
        EmptyMessage.Text = message
        EmptyActionButton.Content = actionText
        EmptyState.Visibility = Visibility.Visible
        SuppliersGrid.Visibility = Visibility.Collapsed
    End Sub

    Private Sub Search_TextChanged(sender As Object, e As TextChangedEventArgs)
        ApplyFilter()
    End Sub

    Private Sub EmptyAction_Click(sender As Object, e As RoutedEventArgs)
        If _suppliers.Count = 0 Then
            UiActions.AddSupplier()
        Else
            SearchBox.Text = ""
        End If
    End Sub

    Private Sub AddSupplier_Click(sender As Object, e As RoutedEventArgs)
        UiActions.AddSupplier()
    End Sub

    Private Shared Function RowSupplier(sender As Object) As Supplier
        Return TryCast(DirectCast(sender, FrameworkElement).DataContext, Supplier)
    End Function

    Private Sub EditRow_Click(sender As Object, e As RoutedEventArgs)
        UiActions.EditSupplier(RowSupplier(sender))
    End Sub

    Private Sub DeleteRow_Click(sender As Object, e As RoutedEventArgs)
        UiActions.DeleteSupplier(RowSupplier(sender))
    End Sub

    Private Sub SuppliersGrid_MouseDoubleClick(sender As Object, e As MouseButtonEventArgs)
        Dim source = TryCast(e.OriginalSource, DependencyObject)
        If source Is Nothing Then Return
        Dim row = TryCast(ItemsControl.ContainerFromElement(SuppliersGrid, source), DataGridRow)
        If row IsNot Nothing Then UiActions.EditSupplier(TryCast(row.Item, Supplier))
    End Sub
End Class
