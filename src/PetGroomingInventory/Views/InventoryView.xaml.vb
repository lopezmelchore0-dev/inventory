Class InventoryView

    Private Const RestockFilter As String = "restock"

    Private _items As New List(Of Item)
    Private _updatingFilters As Boolean

    Public Sub New()
        InitializeComponent()
        _updatingFilters = True
        StatusFilter.ItemsSource = New List(Of FilterOption) From {
            New FilterOption("All stock statuses", Nothing),
            New FilterOption("Needs restocking", RestockFilter),
            New FilterOption("In Stock", StockStatus.InStock),
            New FilterOption("Low Stock", StockStatus.LowStock),
            New FilterOption("Critical Stock", StockStatus.CriticalStock),
            New FilterOption("Out of Stock", StockStatus.OutOfStock)
        }
        ExpiryFilter.ItemsSource = New List(Of FilterOption) From {
            New FilterOption("All expiration statuses", Nothing),
            New FilterOption("Expired", ExpirationStatus.Expired),
            New FilterOption("Expiring Soon", ExpirationStatus.ExpiringSoon),
            New FilterOption("Not expiring soon", ExpirationStatus.Ok),
            New FilterOption("No expiration date", ExpirationStatus.None)
        }
        PetTypeFilter.ItemsSource = PetTypes.FilterOptions(PetTypes.All)
        StatusFilter.SelectedIndex = 0
        ExpiryFilter.SelectedIndex = 0
        PetTypeFilter.SelectedIndex = 0
        CategoryFilter.ItemsSource = New List(Of FilterOption) From {New FilterOption("All categories", Nothing)}
        SupplierFilter.ItemsSource = New List(Of FilterOption) From {New FilterOption("All suppliers", Nothing)}
        CategoryFilter.SelectedIndex = 0
        SupplierFilter.SelectedIndex = 0
        _updatingFilters = False
    End Sub

    Protected Overrides Sub LoadData()
        _items = InventoryService.GetItems()
        RefreshLookupFilters()
        ApplyFilters()
    End Sub

    ''' <summary>Rebuilds the category, pet type and supplier drop-downs, keeping the current selection when it still exists.</summary>
    Private Sub RefreshLookupFilters()
        _updatingFilters = True
        Try
            ReplaceOptions(PetTypeFilter, PetTypes.FilterOptions(_items.Select(Function(i) i.PetType)))

            Dim categories = New List(Of FilterOption) From {New FilterOption("All categories", Nothing)}
            categories.AddRange(LookupService.GetAll(LookupKind.Category).Select(Function(c) New FilterOption(c.Name, c.Id)))
            ReplaceOptions(CategoryFilter, categories)

            Dim suppliers = New List(Of FilterOption) From {New FilterOption("All suppliers", Nothing)}
            suppliers.AddRange(SupplierService.GetAll().Select(Function(s) New FilterOption(s.DisplayName, s.Id)))
            ReplaceOptions(SupplierFilter, suppliers)
        Finally
            _updatingFilters = False
        End Try
    End Sub

    Private Shared Sub ReplaceOptions(combo As ComboBox, options As List(Of FilterOption))
        Dim previous = SelectedValue(combo)
        combo.ItemsSource = options
        Dim match = options.FirstOrDefault(Function(o) Object.Equals(o.Value, previous))
        combo.SelectedItem = If(match, options(0))
    End Sub

    Private Shared Function SelectedValue(combo As ComboBox) As Object
        Return TryCast(combo.SelectedItem, FilterOption)?.Value
    End Function

    Private Sub ApplyFilters()
        If _updatingFilters Then Return
        Dim query As IEnumerable(Of Item) = _items

        Dim text = SearchBox.Text.Trim()
        If text.Length > 0 Then
            query = query.Where(Function(i) i.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                            If(i.Description, "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
        End If

        Dim category = SelectedValue(CategoryFilter)
        If category IsNot Nothing Then
            Dim categoryId = CInt(category)
            query = query.Where(Function(i) i.CategoryId = categoryId)
        End If

        Dim petType = TryCast(SelectedValue(PetTypeFilter), String)
        If petType IsNot Nothing Then
            query = query.Where(Function(i) PetTypes.Matches(i.PetType, petType))
        End If

        Dim supplier = SelectedValue(SupplierFilter)
        If supplier IsNot Nothing Then
            Dim supplierId = CInt(supplier)
            query = query.Where(Function(i) i.SupplierId = supplierId)
        End If

        Dim status = SelectedValue(StatusFilter)
        If TypeOf status Is StockStatus Then
            Dim wanted = DirectCast(status, StockStatus)
            query = query.Where(Function(i) i.Status = wanted)
        ElseIf RestockFilter.Equals(status) Then
            query = query.Where(Function(i) i.NeedsRestock)
        End If

        Dim expiry = SelectedValue(ExpiryFilter)
        If TypeOf expiry Is ExpirationStatus Then
            Dim wanted = DirectCast(expiry, ExpirationStatus)
            query = query.Where(Function(i) i.ExpiryStatus = wanted)
        End If

        Dim result = query.ToList()
        ItemsGrid.ItemsSource = result

        Dim restock = _items.Where(Function(i) i.NeedsRestock).Count()
        CountText.Text = $"{Fmt.Plural(_items.Count, "item")} in inventory"
        CountSubText.Text = $"Showing {result.Count} · {Fmt.Plural(restock, "item")} need restocking"

        If _items.Count = 0 Then
            ShowEmpty("No inventory items yet",
                      "Add your first product to start monitoring stock levels, or load sample data in Settings.", "Add your first item")
        ElseIf result.Count = 0 Then
            ShowEmpty("No items match your filters", "Try a different search, or clear the filters to see all items.", "Clear filters")
        Else
            EmptyState.Visibility = Visibility.Collapsed
            ItemsGrid.Visibility = Visibility.Visible
        End If
    End Sub

    Private Sub ShowEmpty(title As String, message As String, actionText As String)
        EmptyTitle.Text = title
        EmptyMessage.Text = message
        EmptyActionButton.Content = actionText
        EmptyState.Visibility = Visibility.Visible
        ItemsGrid.Visibility = Visibility.Collapsed
    End Sub

    Private Sub ResetFilters()
        _updatingFilters = True
        SearchBox.Text = ""
        CategoryFilter.SelectedIndex = 0
        PetTypeFilter.SelectedIndex = 0
        SupplierFilter.SelectedIndex = 0
        StatusFilter.SelectedIndex = 0
        ExpiryFilter.SelectedIndex = 0
        _updatingFilters = False
    End Sub

    ' ---- Called from the header search and dashboard cards ----

    Public Sub ApplySearch(text As String)
        ResetFilters()
        SearchBox.Text = If(text, "").Trim()
        ApplyFilters()
    End Sub

    Public Sub ShowFilter(status As StockStatus?, expiry As ExpirationStatus?)
        ResetFilters()
        _updatingFilters = True
        If status.HasValue Then
            StatusFilter.SelectedItem = DirectCast(StatusFilter.ItemsSource, List(Of FilterOption)).
                FirstOrDefault(Function(o) TypeOf o.Value Is StockStatus AndAlso DirectCast(o.Value, StockStatus) = status.Value)
        End If
        If expiry.HasValue Then
            ExpiryFilter.SelectedItem = DirectCast(ExpiryFilter.ItemsSource, List(Of FilterOption)).
                FirstOrDefault(Function(o) TypeOf o.Value Is ExpirationStatus AndAlso DirectCast(o.Value, ExpirationStatus) = expiry.Value)
        End If
        _updatingFilters = False
        ApplyFilters()
    End Sub

    ' ---- Event handlers ----

    Private Sub Filter_Changed(sender As Object, e As RoutedEventArgs)
        ApplyFilters()
    End Sub

    Private Sub ClearFilters_Click(sender As Object, e As RoutedEventArgs)
        ResetFilters()
        ApplyFilters()
    End Sub

    Private Sub EmptyAction_Click(sender As Object, e As RoutedEventArgs)
        If _items.Count = 0 Then
            UiActions.AddItem()
        Else
            ResetFilters()
            ApplyFilters()
        End If
    End Sub

    Private Sub AddItem_Click(sender As Object, e As RoutedEventArgs)
        UiActions.AddItem()
    End Sub

    Private Sub StockIn_Click(sender As Object, e As RoutedEventArgs)
        UiActions.StockIn(TryCast(ItemsGrid.SelectedItem, Item)?.Id)
    End Sub

    Private Sub Adjust_Click(sender As Object, e As RoutedEventArgs)
        UiActions.AdjustStock(TryCast(ItemsGrid.SelectedItem, Item)?.Id)
    End Sub

    Private Shared Function RowItem(sender As Object) As Item
        Return TryCast(DirectCast(sender, FrameworkElement).DataContext, Item)
    End Function

    Private Sub EditRow_Click(sender As Object, e As RoutedEventArgs)
        UiActions.EditItem(RowItem(sender))
    End Sub

    Private Sub StockInRow_Click(sender As Object, e As RoutedEventArgs)
        Dim it = RowItem(sender)
        If it IsNot Nothing Then UiActions.StockIn(it.Id)
    End Sub

    Private Sub AdjustRow_Click(sender As Object, e As RoutedEventArgs)
        Dim it = RowItem(sender)
        If it IsNot Nothing Then UiActions.AdjustStock(it.Id)
    End Sub

    Private Sub DeleteRow_Click(sender As Object, e As RoutedEventArgs)
        UiActions.DeleteItem(RowItem(sender))
    End Sub

    Private Sub ItemsGrid_MouseDoubleClick(sender As Object, e As MouseButtonEventArgs)
        Dim source = TryCast(e.OriginalSource, DependencyObject)
        If source Is Nothing Then Return
        Dim row = TryCast(ItemsControl.ContainerFromElement(ItemsGrid, source), DataGridRow)
        If row IsNot Nothing Then UiActions.EditItem(TryCast(row.Item, Item))
    End Sub
End Class
