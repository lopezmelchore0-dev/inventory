Imports System.Globalization

Class DashboardView

    Private Shared ReadOnly Tips As String() = {
        "Record every delivery with Stock In on the day it arrives, so the counts on this dashboard stay accurate.",
        "Use products with the nearest expiration date first — check the Expiration Watch before each grooming day.",
        "Do a quick physical count of shampoos and conditioners every week and fix differences with Stock Adjustment.",
        "Reorder Low Stock items before they turn Critical, especially products your Aparri suppliers deliver weekly.",
        "Keep opened bottles tightly sealed and away from sunlight to avoid early spoilage.",
        "Back up your data regularly in Settings, and copy the backup file to a flash drive or cloud folder.",
        "Set the low-stock threshold in Settings to match how fast your busiest products are used."
    }

    Private _tipIndex As Integer

    Public Sub New()
        InitializeComponent()
        _tipIndex = Date.Today.DayOfYear Mod Tips.Length
        TipText.Text = Tips(_tipIndex)
    End Sub

    Protected Overrides Sub LoadData()
        Dim snapshot = InventoryService.LoadSnapshot()
        Dim s = snapshot.Summary
        Dim settings = snapshot.Settings
        Dim items = snapshot.Items

        ' Banner
        Dim hour = Date.Now.Hour
        GreetingText.Text = If(hour < 12, "Good morning!", If(hour < 18, "Good afternoon!", "Good evening!"))
        DateText.Text = Date.Today.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture) & "  ·  Aparri, Cagayan"
        BannerSummaryText.Text = BuildBannerSummary(s)

        ' Summary cards
        TotalItemsValue.Text = s.TotalItems.ToString("N0", CultureInfo.InvariantCulture)
        AvailableStockValue.Text = Fmt.Qty(s.AvailableStock)
        LowStockValue.Text = s.LowStock.ToString(CultureInfo.InvariantCulture)
        CriticalStockValue.Text = s.CriticalStock.ToString(CultureInfo.InvariantCulture)
        OutOfStockValue.Text = s.OutOfStock.ToString(CultureInfo.InvariantCulture)
        ExpiringSoonValue.Text = s.ExpiringSoon.ToString(CultureInfo.InvariantCulture)
        LowStockCaption.Text = $"{settings.CriticalStockThreshold + 1} to {settings.LowStockThreshold} left"
        CriticalStockCaption.Text = $"{settings.CriticalStockThreshold} or fewer left"
        ExpiringSoonCaption.Text = If(s.Expired > 0, $"Within {settings.ExpiringSoonDays} days · {s.Expired} expired",
                                                     $"Within {settings.ExpiringSoonDays} days")

        ' Stock alerts (most urgent first)
        Dim alerts = items.Where(Function(i) i.NeedsRestock).
                           OrderByDescending(Function(i) i.Status).ThenBy(Function(i) i.Quantity).ThenBy(Function(i) i.Name).ToList()
        AlertsList.ItemsSource = alerts.Take(8).ToList()
        AlertsEmpty.Visibility = If(alerts.Count = 0, Visibility.Visible, Visibility.Collapsed)
        AlertsCountText.Text = If(alerts.Count = 0, "All items are well stocked",
                                  $"{Fmt.Plural(alerts.Count, "item")} need restocking" & If(alerts.Count > 8, " · showing 8 most urgent", ""))
        If s.TotalItems = 0 Then AlertsEmpty.Text = "No inventory items yet. Add items to start monitoring stock levels."

        ' Recent activities
        Dim recent = ActivityService.GetRecent(8)
        RecentList.ItemsSource = recent
        RecentEmpty.Visibility = If(recent.Count = 0, Visibility.Visible, Visibility.Collapsed)

        ' Stock health
        Dim healthy = If(s.TotalItems = 0, 0.0, s.InStock / CDbl(s.TotalItems))
        HealthRing.Value = healthy
        HealthPercentText.Text = If(s.TotalItems = 0, "—", $"{Math.Round(healthy * 100)}%")
        HealthCaption.Text = If(s.TotalItems = 0, "No items to monitor yet", $"of items are In Stock ({s.InStock} of {s.TotalItems})")
        Dim total = Math.Max(1, s.TotalItems)
        LowRing.Value = s.LowStock / CDbl(total)
        CriticalRing.Value = s.CriticalStock / CDbl(total)
        OutRing.Value = s.OutOfStock / CDbl(total)
        LowRingText.Text = s.LowStock.ToString(CultureInfo.InvariantCulture)
        CriticalRingText.Text = s.CriticalStock.ToString(CultureInfo.InvariantCulture)
        OutRingText.Text = s.OutOfStock.ToString(CultureInfo.InvariantCulture)

        ' Expiration watch (expired first, then nearest date)
        Dim expiring = items.Where(Function(i) i.HasExpirationAlert).OrderBy(Function(i) i.ExpirationDate).Take(5).ToList()
        ExpiringList.ItemsSource = expiring
        ExpiringEmpty.Text = $"No items expired or expiring within {settings.ExpiringSoonDays} days."
        ExpiringEmpty.Visibility = If(expiring.Count = 0, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Shared Function BuildBannerSummary(s As DashboardSummary) As String
        If s.TotalItems = 0 Then
            Return "Your inventory is empty. Add your first item, or load sample data in Settings to explore the system."
        End If
        Dim parts As New List(Of String)
        If s.NeedsRestock > 0 Then parts.Add($"{Fmt.Plural(s.NeedsRestock, "item")} need restocking")
        If s.Expired > 0 Then parts.Add($"{Fmt.Plural(s.Expired, "item")} expired")
        If s.ExpiringSoon > 0 Then parts.Add($"{Fmt.Plural(s.ExpiringSoon, "item")} expiring soon")
        If parts.Count = 0 Then Return $"All {Fmt.Plural(s.TotalItems, "item")} are well stocked and nothing is expiring soon."
        Return $"You are tracking {Fmt.Plural(s.TotalItems, "item")}: " & String.Join(", ", parts) & "."
    End Function

    ' ---- Responsive layout ----

    Private Sub LayoutRoot_SizeChanged(sender As Object, e As SizeChangedEventArgs)
        Dim width = LayoutRoot.ActualWidth
        Dim narrow = width < 1060

        If narrow Then
            SideColumn.Width = New GridLength(0)
            Grid.SetColumn(SidePanel, 0)
            Grid.SetRow(SidePanel, 1)
            SidePanel.Margin = New Thickness(0, 16, 0, 0)
            PlaceSideCard(HealthCard, 0, 0, 1, New Thickness(0, 0, 8, 0))
            PlaceSideCard(ExpiryCard, 0, 1, 1, New Thickness(8, 0, 8, 0))
            PlaceSideCard(TipCard, 0, 2, 1, New Thickness(8, 0, 0, 0))
        Else
            SideColumn.Width = New GridLength(320)
            Grid.SetColumn(SidePanel, 1)
            Grid.SetRow(SidePanel, 0)
            SidePanel.Margin = New Thickness(16, 0, 0, 0)
            PlaceSideCard(HealthCard, 0, 0, 3, New Thickness(0))
            PlaceSideCard(ExpiryCard, 1, 0, 3, New Thickness(0, 16, 0, 0))
            PlaceSideCard(TipCard, 2, 0, 3, New Thickness(0, 16, 0, 0))
        End If

        Dim mainWidth = If(narrow, width, width - 336)
        SummaryGrid.Columns = If(mainWidth >= 1200, 6, 3)
        BannerArt.Visibility = If(mainWidth < 620, Visibility.Collapsed, Visibility.Visible)
    End Sub

    Private Shared Sub PlaceSideCard(card As FrameworkElement, row As Integer, column As Integer, span As Integer, margin As Thickness)
        Grid.SetRow(card, row)
        Grid.SetColumn(card, column)
        Grid.SetColumnSpan(card, span)
        card.Margin = margin
    End Sub

    ' ---- Navigation and quick actions ----

    Private Shared Function OpenInventory() As InventoryView
        Return TryCast(MainWindow.Instance.NavigateTo(PageKey.Inventory), InventoryView)
    End Function

    Private Sub QuickAddItem_Click(sender As Object, e As RoutedEventArgs)
        UiActions.AddItem()
    End Sub

    Private Sub QuickStockIn_Click(sender As Object, e As RoutedEventArgs)
        UiActions.StockIn()
    End Sub

    Private Sub QuickAlerts_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance.NavigateTo(PageKey.StockMonitoring)
    End Sub

    Private Sub QuickSuppliers_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance.NavigateTo(PageKey.Suppliers)
    End Sub

    Private Sub CardTotal_Click(sender As Object, e As RoutedEventArgs)
        OpenInventory()?.ShowFilter(Nothing, Nothing)
    End Sub

    Private Sub CardLow_Click(sender As Object, e As RoutedEventArgs)
        OpenInventory()?.ShowFilter(StockStatus.LowStock, Nothing)
    End Sub

    Private Sub CardCritical_Click(sender As Object, e As RoutedEventArgs)
        OpenInventory()?.ShowFilter(StockStatus.CriticalStock, Nothing)
    End Sub

    Private Sub CardOut_Click(sender As Object, e As RoutedEventArgs)
        OpenInventory()?.ShowFilter(StockStatus.OutOfStock, Nothing)
    End Sub

    Private Sub CardExpiring_Click(sender As Object, e As RoutedEventArgs)
        OpenInventory()?.ShowFilter(Nothing, ExpirationStatus.ExpiringSoon)
    End Sub

    Private Sub ViewAllAlerts_Click(sender As Object, e As RoutedEventArgs)
        TryCast(MainWindow.Instance.NavigateTo(PageKey.StockMonitoring), StockMonitoringView)?.SelectTab(0)
    End Sub

    Private Sub ViewAllExpiry_Click(sender As Object, e As RoutedEventArgs)
        TryCast(MainWindow.Instance.NavigateTo(PageKey.StockMonitoring), StockMonitoringView)?.SelectTab(1)
    End Sub

    Private Sub ViewAllActivities_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance.NavigateTo(PageKey.Activities)
    End Sub

    Private Sub NextTip_Click(sender As Object, e As RoutedEventArgs)
        _tipIndex = (_tipIndex + 1) Mod Tips.Length
        TipText.Text = Tips(_tipIndex)
    End Sub
End Class
