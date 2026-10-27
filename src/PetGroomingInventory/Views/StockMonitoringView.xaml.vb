Imports System.Globalization

Class StockMonitoringView

    Public Sub New()
        InitializeComponent()
    End Sub

    Protected Overrides Sub LoadData()
        Dim snapshot = InventoryService.LoadSnapshot()
        Dim s = snapshot.Summary
        Dim settings = snapshot.Settings

        InStockCount.Text = s.InStock.ToString(CultureInfo.InvariantCulture)
        LowCount.Text = s.LowStock.ToString(CultureInfo.InvariantCulture)
        CriticalCount.Text = s.CriticalStock.ToString(CultureInfo.InvariantCulture)
        OutCount.Text = s.OutOfStock.ToString(CultureInfo.InvariantCulture)
        RulesText.Text = $"Low ≤ {settings.LowStockThreshold} · Critical ≤ {settings.CriticalStockThreshold} · Out = 0"

        ' Most urgent first: Out of Stock → Critical → Low, then lowest quantity.
        Dim restock = snapshot.Items.Where(Function(i) i.NeedsRestock).
            OrderByDescending(Function(i) i.Status).ThenBy(Function(i) i.Quantity).ThenBy(Function(i) i.Name).ToList()
        RestockGrid.ItemsSource = restock
        RestockTab.Header = $"Needs Restocking ({restock.Count})"
        SetEmpty(RestockGrid, RestockEmpty, restock.Count = 0,
                 If(snapshot.Items.Count = 0, "No inventory items yet.", "All items are well stocked. Nothing needs restocking."))

        Dim expiring = snapshot.Items.Where(Function(i) i.HasExpirationAlert).OrderBy(Function(i) i.ExpirationDate).ToList()
        ExpiryGrid.ItemsSource = expiring
        ExpiryTab.Header = $"Expiration Alerts ({expiring.Count})"
        SetEmpty(ExpiryGrid, ExpiryEmpty, expiring.Count = 0,
                 $"No items are expired or expiring within {settings.ExpiringSoonDays} days.{vbLf}Items without an expiration date never appear here.")

        Dim stockIns = StockService.GetHistory(adjustments:=False)
        StockInGrid.ItemsSource = stockIns
        SetEmpty(StockInGrid, StockInEmpty, stockIns.Count = 0, Nothing)

        Dim adjustments = StockService.GetHistory(adjustments:=True)
        AdjustmentGrid.ItemsSource = adjustments
        SetEmpty(AdjustmentGrid, AdjustmentEmpty, adjustments.Count = 0, Nothing)
    End Sub

    Private Shared Sub SetEmpty(grid As DataGrid, label As TextBlock, isEmpty As Boolean, message As String)
        If message IsNot Nothing Then label.Text = message
        label.Visibility = If(isEmpty, Visibility.Visible, Visibility.Collapsed)
        grid.Visibility = If(isEmpty, Visibility.Collapsed, Visibility.Visible)
    End Sub

    Public Sub SelectTab(index As Integer)
        If index >= 0 AndAlso index < Tabs.Items.Count Then Tabs.SelectedIndex = index
    End Sub

    Private Sub StockIn_Click(sender As Object, e As RoutedEventArgs)
        UiActions.StockIn()
    End Sub

    Private Sub Adjust_Click(sender As Object, e As RoutedEventArgs)
        UiActions.AdjustStock()
    End Sub

    Private Sub RestockRow_Click(sender As Object, e As RoutedEventArgs)
        Dim item = TryCast(DirectCast(sender, FrameworkElement).DataContext, Item)
        If item IsNot Nothing Then UiActions.StockIn(item.Id)
    End Sub

    Private Sub AdjustRow_Click(sender As Object, e As RoutedEventArgs)
        Dim item = TryCast(DirectCast(sender, FrameworkElement).DataContext, Item)
        If item IsNot Nothing Then UiActions.AdjustStock(item.Id)
    End Sub
End Class
