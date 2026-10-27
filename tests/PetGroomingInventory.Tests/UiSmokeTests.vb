Imports System.Reflection
Imports System.Threading
Imports System.Windows
Imports System.Windows.Controls
Imports Xunit

''' <summary>
''' Builds every page and dialog off-screen (nothing is shown) with sample data loaded, so XAML,
''' styles, templates and data binding problems surface as test failures. Requires the test database.
''' </summary>
<Collection("Database")>
Public Class UiSmokeTests
    Implements IDisposable

    Private Const TestDatabase As String = "pawstock_ui_smoke_test"

    Public Sub Dispose() Implements IDisposable.Dispose
        DatabaseIntegrationTests.DropDatabase(TestDatabase)
    End Sub

    <IntegrationFact>
    Public Sub AllPagesAndDialogs_LoadWithData()
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    RunSmokeTest()
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        thread.Join()
        If failure IsNot Nothing Then Throw New Exception("UI smoke test failed: " & failure.ToString(), failure)
    End Sub

    Private Shared Sub RunSmokeTest()
        DatabaseIntegrationTests.ResetDatabase(TestDatabase)
        SampleDataService.Load()

        Dim app As New Global.PetGroomingInventory.Application()
        app.InitializeComponent()

        PasswordHasher.Iterations = 10000
        RememberedLogin.FilePath = IO.Path.Combine(IO.Path.GetTempPath(), "pawstock-tests", "login.json")
        AuthSession.SignOut()

        Dim shell As New MainWindow()
        Layout(shell)

        ' ---- Sign-in gate: no page is reachable without a session ----
        Assert.Null(shell.NavigateTo(PageKey.Dashboard))
        Assert.Null(shell.PageHost.Content)
        Assert.Equal(Visibility.Collapsed, shell.ShellRoot.Visibility)
        Assert.IsType(Of RegisterView)(shell.AuthPanel.Content)   ' first run: no Admin yet
        Layout(shell)

        AuthService.Register(New RegistrationInput With {
            .FullName = "Maria Santos", .Username = "maria", .Password = "Grooming#2026", .ConfirmPassword = "Grooming#2026",
            .SecurityQuestion = SecurityQuestions.All(0), .SecurityAnswer = "Bantay"})
        shell.ShowAuth()
        Assert.IsType(Of LoginView)(shell.AuthPanel.Content)
        shell.ShowAuth(AuthScreen.Register)                        ' registration is closed once an Admin exists
        Assert.IsType(Of LoginView)(shell.AuthPanel.Content)
        shell.ShowAuth(AuthScreen.ForgotPassword)
        Assert.IsType(Of ForgotPasswordView)(shell.AuthPanel.Content)
        Layout(shell)

        Dim authViews As FrameworkElement() = {New LoginView(True), New LoginView(False, "Notice", "maria"), New RegisterView(),
                                               New ForgotPasswordView(), New PasswordField With {.ShowStrength = True, .Password = "Ab1!"}}
        For Each view In authViews
            Layout(view)
        Next

        AuthService.Login("maria", "Grooming#2026")
        shell.OnSignedIn()
        Assert.IsType(Of DashboardView)(shell.PageHost.Content)
        Assert.Equal(Visibility.Visible, shell.ShellRoot.Visibility)
        Assert.Equal(Visibility.Collapsed, shell.AuthPanel.Visibility)
        Assert.Equal("Maria Santos", shell.AccountNameText.Text)
        For Each key In {PageKey.Profile, PageKey.ChangePassword, PageKey.AddAdmin, PageKey.Settings, PageKey.Reports}
            Assert.NotNull(shell.NavigateTo(key))
            Layout(shell)
        Next

        Dim loadData = GetType(PageBase).GetMethod("LoadData", BindingFlags.NonPublic Or BindingFlags.Instance)
        Dim pages As PageBase() = {New DashboardView(), New InventoryView(), New StockMonitoringView(),
                                   New SuppliersView(), New ActivitiesView(), New SettingsView(),
                                   New ProfileView(), New ChangePasswordView(), New ReportsView()}
        For Each page In pages
            loadData.Invoke(page, Nothing)
            Layout(page)
        Next

        ' Page helpers used by navigation
        Dim inventory = DirectCast(pages(1), InventoryView)
        inventory.ShowFilter(StockStatus.LowStock, Nothing)
        inventory.ApplySearch("shampoo")
        DirectCast(pages(2), StockMonitoringView).SelectTab(1)
        Layout(pages(2))
        CheckReports(DirectCast(pages(8), ReportsView))

        Dim item = InventoryService.GetItems().First()
        Dim supplier = SupplierService.GetAll().First()
        Dim dialogs As Window() = {
            New ItemEditorWindow(Nothing), New ItemEditorWindow(item),
            New SupplierEditorWindow(Nothing), New SupplierEditorWindow(supplier),
            New StockInWindow(Nothing), New StockInWindow(item.Id),
            New StockAdjustmentWindow(item.Id),
            New MessageDialog("Title", "Message", MessageKind.Danger, "OK", "Cancel")
        }
        For Each dialog In dialogs
            Layout(dialog)
        Next
        Assert.True(DirectCast(dialogs(4), StockInWindow).HasItems)

        CheckDescriptionBox()

        ' ---- Logout path: session ends, cached pages are dropped, Login is shown ----
        AuthService.Logout()
        shell.ShowAuth(AuthScreen.Login, "You have been logged out.")
        Assert.Null(shell.PageHost.Content)
        Assert.Equal(Visibility.Collapsed, shell.ShellRoot.Visibility)
        Assert.IsType(Of LoginView)(shell.AuthPanel.Content)
        Assert.Null(shell.NavigateTo(PageKey.Inventory))
        Assert.Null(shell.PageHost.Content)
    End Sub

    ''' <summary>Every report type runs against the sample data, and a generated report renders on the Reports page.</summary>
    Private Shared Sub CheckReports(view As ReportsView)
        Dim itemCount = InventoryService.GetItems().Count
        Dim bounds = ReportService.GetDateBounds()
        Assert.True(bounds.Earliest.HasValue AndAlso bounds.Latest.HasValue)
        Dim showReport = GetType(ReportsView).GetMethod("ShowReport", BindingFlags.NonPublic Or BindingFlags.Instance)
        For Each type In ReportTypes.All
            Dim report = ReportService.Generate(New ReportCriteria With {.Type = type, .FromDate = bounds.Earliest, .ToDate = bounds.Latest})
            Assert.Equal(ReportTypes.Title(type), report.Title)
            showReport.Invoke(view, {report})
            Layout(view)
        Next
        ' Over all time every sample item was added, and the category totals cover every item exactly once.
        Dim list = ReportService.Generate(New ReportCriteria With {.Type = ReportType.InventoryList, .FromDate = bounds.Earliest, .ToDate = bounds.Latest})
        Assert.Equal(itemCount, list.Tables.Single().Rows.Count)
        Dim byCategory = ReportService.Generate(New ReportCriteria With {.Type = ReportType.Category})
        Assert.Equal(itemCount, CInt(byCategory.Tables.Single().Totals(1)))
        Dim movement = ReportService.Generate(New ReportCriteria With {.Type = ReportType.Movement, .FromDate = bounds.Earliest, .ToDate = bounds.Latest})
        Assert.False(movement.IsEmpty)
    End Sub

    ''' <summary>The Description field is an editable dropdown: suggestions open on focus, narrow as the user types, and any text is kept.</summary>
    Private Shared Sub CheckDescriptionBox()
        Dim editor = ShowOffScreen(New ItemEditorWindow(Nothing))
        Try
            Dim combo = editor.DescriptionBox
            Dim box = DirectCast(combo.Template.FindName("PART_EditableTextBox", combo), TextBox)
            Assert.True(combo.IsEditable)
            Assert.Equal(InventoryService.MaxDescriptionLength, box.MaxLength)
            Dim visible = Function() combo.Items.Cast(Of String)().ToList()
            Dim pump = Sub() combo.Dispatcher.Invoke(Sub() Exit Sub, Threading.DispatcherPriority.Background)
            ' Types into the field the way a user would: the box must have keyboard focus for the text to count as typed.
            Dim typeText =
                Sub(text As String)
                    editor.Activate()
                    box.Focus()
                    Assert.True(box.IsKeyboardFocused, "The description text box should take keyboard focus")
                    box.Text = text
                    pump()
                End Sub

            Assert.Equal("", combo.Text)                 ' a new item starts blank, not with the first suggestion
            Assert.Contains("Lamb", visible())
            Assert.Contains("ml", visible())

            editor.Activate()
            box.Focus()
            Assert.True(combo.IsDropDownOpen, "Focusing the field should show the suggestions")

            typeText("ora")                              ' narrows while typing
            Assert.Equal({"Orange"}, visible())
            Assert.Equal("ora", combo.Text)

            typeText("15 g Pink")                        ' no suggestion matches: the list closes, the custom text stays
            Assert.Empty(visible())
            Assert.False(combo.IsDropDownOpen)
            Assert.Equal("15 g Pink", combo.Text)

            typeText("")                                 ' cleared: everything is offered again
            Assert.True(visible().Count >= DescriptionSuggestions.Defaults.Length)

            combo.SelectedItem = "Lamb"                  ' picking a suggestion fills the field and keeps the full list
            pump()
            Assert.Equal("Lamb", combo.Text)
            Assert.True(visible().Count >= DescriptionSuggestions.Defaults.Length)
            typeText("Lamb 500 g")                       ' a picked value can be edited freely
            Assert.Equal("Lamb 500 g", combo.Text)
        Finally
            editor.Close()
        End Try

        ' Edit Item loads a saved description even when it is not one of the suggestions, and leaves it alone.
        Dim item = InventoryService.GetItems().First()
        item.Description = "Custom 123 flavor, 2 x 250 g"
        Dim edit = ShowOffScreen(New ItemEditorWindow(item))
        Try
            Dim combo = edit.DescriptionBox
            Dim box = DirectCast(combo.Template.FindName("PART_EditableTextBox", combo), TextBox)
            Assert.Equal("Custom 123 flavor, 2 x 250 g", combo.Text)
            edit.Activate()
            box.Focus()
            combo.Dispatcher.Invoke(Sub() Exit Sub, Threading.DispatcherPriority.Background)
            Assert.Equal("Custom 123 flavor, 2 x 250 g", combo.Text)
            Assert.True(combo.Items.Count >= DescriptionSuggestions.Defaults.Length, "Opening an existing item still lists every suggestion")
        Finally
            edit.Close()
        End Try
    End Sub

    Private Shared Function ShowOffScreen(window As ItemEditorWindow) As ItemEditorWindow
        window.WindowStartupLocation = WindowStartupLocation.Manual
        window.Left = -10000
        window.Top = -10000
        window.ShowInTaskbar = False
        window.Show()
        Return window
    End Function

    ''' <summary>Applies templates and runs layout without showing anything on screen.</summary>
    Private Shared Sub Layout(element As FrameworkElement)
        element.ApplyTemplate()
        Dim target As FrameworkElement = element
        If TypeOf element Is Window Then target = DirectCast(DirectCast(element, Window).Content, FrameworkElement)
        target.Measure(New Size(1400, 900))
        target.Arrange(New Rect(0, 0, 1400, 900))
        target.UpdateLayout()
    End Sub
End Class
