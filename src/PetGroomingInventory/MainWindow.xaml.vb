Imports System.Windows.Media.Animation
Imports System.Windows.Media.Effects
Imports System.Windows.Threading

Public Enum PageKey
    Dashboard
    Inventory
    StockMonitoring
    Suppliers
    Activities
    Reports
    Settings
    Profile
    ChangePassword
    Help
End Enum

Public Enum ToastKind
    Success
    Info
    [Error]
End Enum

Class MainWindow

    Public Shared Property Instance As MainWindow

    Private ReadOnly _pages As New Dictionary(Of PageKey, PageBase)
    Private ReadOnly _navButtons As Dictionary(Of PageKey, RadioButton)
    Private _overlayCount As Integer
    Private _suppressNav As Boolean

    Public Sub New()
        InitializeComponent()
        Instance = Me
        _navButtons = New Dictionary(Of PageKey, RadioButton) From {
            {PageKey.Dashboard, NavDashboard},
            {PageKey.Inventory, NavInventory},
            {PageKey.StockMonitoring, NavStock},
            {PageKey.Suppliers, NavSuppliers},
            {PageKey.Activities, NavActivities},
            {PageKey.Reports, NavReports},
            {PageKey.Settings, NavSettings}
        }
        AddHandler AppEvents.DataChanged, Sub(s, e) RefreshShell()
    End Sub

    Private Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ConnectDatabase()
    End Sub

    ' ===================== Database =====================

    Private Sub ConnectDatabase()
        Try
            Mouse.OverrideCursor = Cursors.Wait
            DatabaseInitializer.Initialize()
            DbErrorPanel.Visibility = Visibility.Collapsed
            RefreshShell()
            If AuthSession.IsAuthenticated Then
                Dim current = _pages.FirstOrDefault(Function(p) PageHost.Content Is p.Value)
                NavigateTo(If(current.Value Is Nothing, PageKey.Dashboard, current.Key))
            ElseIf AuthPanel.Content Is Nothing Then
                ShowAuth()
            End If
        Catch ex As Exception
            ShowDbError(ex)
        Finally
            Mouse.OverrideCursor = Nothing
        End Try
    End Sub

    Public Sub ShowDbError(ex As Exception)
        DbErrorMessage.Text = ErrorText.Describe(ex)
        DbConfigText.Text = $"Server: {Db.Config.Server}:{Db.Config.Port}    Database: {Db.Config.Database}    User: {Db.Config.User}" &
                            $"{Environment.NewLine}Connection settings file: {DbConfig.FilePath}"
        DbErrorPanel.Visibility = Visibility.Visible
    End Sub

    Private Sub RetryDb_Click(sender As Object, e As RoutedEventArgs)
        Db.Config = DbConfig.Load()
        ConnectDatabase()
    End Sub

    Private Sub OpenDbConfig_Click(sender As Object, e As RoutedEventArgs)
        Try
            Db.Config.SaveIfMissing()
            Process.Start(New ProcessStartInfo("notepad.exe", $"""{DbConfig.FilePath}""") With {.UseShellExecute = True})
        Catch ex As Exception
            Dialogs.ShowError("Couldn't open the settings file", ex.Message)
        End Try
    End Sub

    ' ===================== Authentication =====================

    ''' <summary>
    ''' Ends any session and shows Login, Register or Forgot Password in place of the app.
    ''' With no screen given: Register on first run (no Admin yet), otherwise Login.
    ''' Every cached page is discarded so nothing from the previous session stays reachable.
    ''' </summary>
    Public Sub ShowAuth(Optional screen As AuthScreen? = Nothing, Optional notice As String = Nothing, Optional username As String = Nothing)
        AuthSession.SignOut()
        ClearPages()

        Dim hasAdmin As Boolean
        Try
            hasAdmin = AuthService.HasAnyAdmin()
        Catch ex As Exception
            If ErrorText.IsConnectionError(ex) Then
                ShowDbError(ex)
                Return
            End If
            Throw
        End Try

        Dim target = If(screen, If(hasAdmin, AuthScreen.Login, AuthScreen.Register))
        If target = AuthScreen.Register AndAlso hasAdmin Then
            ' First-run registration only.
            target = AuthScreen.Login
            notice = AuthService.RegistrationClosedMessage
        End If

        Dim view As UserControl
        Select Case target
            Case AuthScreen.Register : view = New RegisterView()
            Case AuthScreen.ForgotPassword : view = New ForgotPasswordView()
            Case Else : view = New LoginView(registrationOpen:=Not hasAdmin, notice:=notice, username:=username)
        End Select
        AuthPanel.Content = view
        AuthPanel.Visibility = Visibility.Visible
    End Sub

    ''' <summary>Called by the Login screen after AuthService.Login succeeded.</summary>
    Public Sub OnSignedIn()
        If Not AuthSession.IsAuthenticated Then
            ShowAuth(AuthScreen.Login)
            Return
        End If
        ClearPages()
        AuthPanel.Content = Nothing
        AuthPanel.Visibility = Visibility.Collapsed
        ShellRoot.Visibility = Visibility.Visible
        RefreshShell()
        NavigateTo(PageKey.Dashboard)
        ShowToast($"Welcome, {AuthSession.Current.FullName}!", ToastKind.Info)
    End Sub

    Private Sub Logout_Click(sender As Object, e As RoutedEventArgs)
        If Not Dialogs.Confirm("Log out", "Are you sure you want to log out?", "Log Out") Then Return
        AuthService.Logout()
        ShowAuth(AuthScreen.Login, "You have been logged out.")
    End Sub

    Private Sub ClearPages()
        ShellRoot.Visibility = Visibility.Collapsed
        PageHost.Content = Nothing
        _pages.Clear()
        GlobalSearchBox.Clear()
        _suppressNav = True
        For Each button In _navButtons.Values
            button.IsChecked = False
        Next
        _suppressNav = False
    End Sub

    Private Sub UpdateAccountCard()
        Dim account = AuthSession.Current
        If account Is Nothing Then Return
        AccountNameText.Text = account.FullName
        AccountNameText.ToolTip = $"{account.FullName} (@{account.Username})"
        AccountInitialsText.Text = account.Initials
    End Sub

    ' ===================== Navigation =====================

    ''' <summary>The single entry point to every page. Returns Nothing (and shows Login) when no Admin is signed in.</summary>
    Public Function NavigateTo(key As PageKey) As PageBase
        If Not AuthSession.IsAuthenticated Then
            ShowAuth()
            Return Nothing
        End If

        Dim page As PageBase = Nothing
        If Not _pages.TryGetValue(key, page) Then
            page = CreatePage(key)
            _pages(key) = page
        End If

        Dim alreadyShown = PageHost.Content Is page
        PageHost.Content = page
        Dim header = PageHeader(key)
        PageTitleText.Text = header.Title
        PageSubtitleText.Text = header.Subtitle

        _suppressNav = True
        Dim navButton As RadioButton = Nothing
        If _navButtons.TryGetValue(key, navButton) Then
            navButton.IsChecked = True
        Else
            For Each button In _navButtons.Values
                button.IsChecked = False
            Next
        End If
        _suppressNav = False

        If alreadyShown Then page.SafeReload()
        Return page
    End Function

    Private Shared Function CreatePage(key As PageKey) As PageBase
        Select Case key
            Case PageKey.Inventory : Return New InventoryView()
            Case PageKey.StockMonitoring : Return New StockMonitoringView()
            Case PageKey.Suppliers : Return New SuppliersView()
            Case PageKey.Activities : Return New ActivitiesView()
            Case PageKey.Reports : Return New ReportsView()
            Case PageKey.Settings : Return New SettingsView()
            Case PageKey.Profile : Return New ProfileView()
            Case PageKey.ChangePassword : Return New ChangePasswordView()
            Case PageKey.Help : Return New HelpView()
            Case Else : Return New DashboardView()
        End Select
    End Function

    Private Shared Function PageHeader(key As PageKey) As (Title As String, Subtitle As String)
        Select Case key
            Case PageKey.Inventory : Return ("Inventory", "View, search, add, edit and remove products.")
            Case PageKey.StockMonitoring : Return ("Stock Monitoring", "Track stock levels, restock items and review stock movements.")
            Case PageKey.Suppliers : Return ("Suppliers", "Manage the suppliers you buy products from.")
            Case PageKey.Activities : Return ("Activities", "A complete history of inventory operations.")
            Case PageKey.Reports : Return ("Reports", "Generate, print and export inventory reports for any period.")
            Case PageKey.Settings : Return ("Settings", "Stock rules, categories and units, backups and sample data.")
            Case PageKey.Profile : Return ("Admin Profile", "Your account details, security question and password.")
            Case PageKey.ChangePassword : Return ("Change Password", "Keep your account secure with a strong password.")
            Case PageKey.Help : Return ("Help & Support", "Quick answers and step-by-step guides for using PawStock.")
            Case Else : Return ("Dashboard", "Welcome back! Here's your shop's inventory at a glance.")
        End Select
    End Function

    Private Sub Nav_Checked(sender As Object, e As RoutedEventArgs)
        If _suppressNav OrElse _navButtons Is Nothing Then Return
        Dim key = _navButtons.First(Function(kv) kv.Value Is sender).Key
        NavigateTo(key)
    End Sub

    Private Sub GlobalSearch_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key <> Key.Enter Then Return
        TryCast(NavigateTo(PageKey.Inventory), InventoryView)?.ApplySearch(GlobalSearchBox.Text)
        e.Handled = True
    End Sub

    Private Sub Alerts_Click(sender As Object, e As RoutedEventArgs)
        NavigateTo(PageKey.StockMonitoring)
    End Sub

    Private Sub Activities_Click(sender As Object, e As RoutedEventArgs)
        NavigateTo(PageKey.Activities)
    End Sub

    Private Sub Profile_Click(sender As Object, e As RoutedEventArgs)
        NavigateTo(PageKey.Profile)
    End Sub

    ''' <summary>Always lands on the Help &amp; Support preview, even if the guide was left open.</summary>
    Private Sub Help_Click(sender As Object, e As RoutedEventArgs)
        TryCast(NavigateTo(PageKey.Help), HelpView)?.ShowPreview()
    End Sub

    ' ===================== Shell state =====================

    Private Sub RefreshShell()
        Try
            If Not AuthSession.IsAuthenticated Then
                ' Only the shop name is shown before sign-in (on the Login card).
                Dim settings = SettingsService.GetSettings()
                AppNameText.Text = settings.AppName
                Title = $"{settings.AppName} – Pet Grooming Inventory"
                Return
            End If
            Dim snapshot = InventoryService.LoadSnapshot()
            AppNameText.Text = snapshot.Settings.AppName
            Title = $"{snapshot.Settings.AppName} – Pet Grooming Inventory"
            Dim alerts = snapshot.Summary.TotalAlerts
            AlertBadge.Visibility = If(alerts > 0, Visibility.Visible, Visibility.Collapsed)
            AlertBadgeText.Text = If(alerts > 99, "99+", alerts.ToString())
            UpdateAccountCard()
        Catch ex As Exception
            ' Best effort: pages report their own load errors.
        End Try
    End Sub

    Public Sub SetOverlay(show As Boolean)
        _overlayCount = Math.Max(0, _overlayCount + If(show, 1, -1))
        ModalOverlay.Visibility = If(_overlayCount > 0, Visibility.Visible, Visibility.Collapsed)
    End Sub

    ' ===================== Toasts =====================

    Public Sub ShowToast(message As String, Optional kind As ToastKind = ToastKind.Success)
        If String.IsNullOrWhiteSpace(message) Then Return
        Dim glyph = If(kind = ToastKind.Success, ChrW(&HE73E), If(kind = ToastKind.Error, ChrW(&HE7BA), ChrW(&HE946)))

        Dim icon As New Border With {
            .Width = 28, .Height = 28, .CornerRadius = New CornerRadius(14),
            .Background = StatusPalette.Find(If(kind = ToastKind.Error, "NeutralDarkBrush", "PrimaryBrush")),
            .VerticalAlignment = VerticalAlignment.Top,
            .Child = New TextBlock With {
                .Text = glyph, .FontFamily = CType(FindResource("IconFont"), FontFamily), .FontSize = 13,
                .Foreground = Brushes.White, .HorizontalAlignment = HorizontalAlignment.Center, .VerticalAlignment = VerticalAlignment.Center
            }
        }
        Dim text As New TextBlock With {
            .Text = message, .Foreground = Brushes.White, .FontSize = 13.5, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(12, 4, 4, 0), .MaxWidth = 360
        }
        Dim content As New StackPanel With {.Orientation = Orientation.Horizontal}
        content.Children.Add(icon)
        content.Children.Add(text)

        Dim toast As New Border With {
            .Background = StatusPalette.Find("ToastBrush"), .CornerRadius = New CornerRadius(14),
            .Padding = New Thickness(14, 12, 18, 12), .Margin = New Thickness(0, 10, 0, 0), .Child = content, .Opacity = 0,
            .Effect = New DropShadowEffect With {.BlurRadius = 18, .ShadowDepth = 4, .Opacity = 0.25, .Color = Color.FromRgb(CByte(&H6B), CByte(&H2A), CByte(&H50))}
        }
        ToastHost.Children.Add(toast)
        toast.BeginAnimation(OpacityProperty, New DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)))

        Dim timer As New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(If(kind = ToastKind.Error, 6, 4))}
        AddHandler timer.Tick,
            Sub(s, e)
                timer.Stop()
                Dim fade As New DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250))
                AddHandler fade.Completed, Sub(s2, e2) ToastHost.Children.Remove(toast)
                toast.BeginAnimation(OpacityProperty, fade)
            End Sub
        timer.Start()
    End Sub
End Class
