Imports System.Reflection
Imports System.Threading
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Threading
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
        CheckItemSearchBox()

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

    ''' <summary>
    ''' The Item field of Stock Adjustment and Stock In is a searchable picker: it lists every item on focus, narrows as the
    ''' user types, says when nothing matches, and only a picked item (never typed text alone) is submitted.
    ''' </summary>
    Private Shared Sub CheckItemSearchBox()
        Dim all = InventoryService.GetItems()
        Dim target = all.First(Function(i) i.Name.Length >= 6 AndAlso
                                           all.Where(Function(j) String.Equals(j.Name, i.Name, StringComparison.OrdinalIgnoreCase)).Count() = 1)
        Dim fragment = target.Name.Substring(1, 4).ToLowerInvariant()   ' part of the name, in the wrong case
        Dim pump = Sub() Dispatcher.CurrentDispatcher.Invoke(Sub() Exit Sub, Threading.DispatcherPriority.Background)

        Dim adjust = ShowOffScreen(New StockAdjustmentWindow(Nothing))
        Try
            Dim combo = adjust.ItemBox
            Dim box = DirectCast(combo.Template.FindName("PART_EditableTextBox", combo), TextBox)
            Dim visible = Function() combo.Items.Cast(Of Item)().ToList()
            Dim focusBox =
                Sub()
                    adjust.Activate()
                    box.Focus()
                    Assert.True(box.IsKeyboardFocused, "The item search box should take keyboard focus")
                End Sub
            Dim typeText =
                Sub(text As String)
                    focusBox()
                    box.Text = text
                    pump()
                End Sub

            Assert.True(combo.IsEditable)
            Assert.Equal("", combo.Text)
            Assert.Null(combo.SelectedItem)
            focusBox()
            pump()
            Assert.True(combo.IsDropDownOpen, "Focusing the field should list the items")
            Assert.Equal(all.Count, visible().Count)

            ' Arrow keys move through the list without narrowing it; Enter picks the highlighted item.
            PressKey(box, Key.Down)
            PressKey(box, Key.Down)
            pump()
            Assert.Equal(all.Count, visible().Count)
            PressKey(box, Key.Enter)
            pump()
            Dim arrowPick = TryCast(combo.SelectedItem, Item)
            Assert.NotNull(arrowPick)
            Assert.Equal(arrowPick.Name, combo.Text)

            typeText(fragment)                                   ' typing narrows the list and drops the earlier pick
            Assert.Contains(visible(), Function(i) i.Id = target.Id)
            Assert.All(visible(), Sub(i) Assert.True(ItemSearchBox.Matches(i, fragment)))
            Assert.Equal(fragment, combo.Text)
            Assert.Null(combo.SelectedItem)
            Assert.Equal(Visibility.Collapsed, adjust.CurrentPanel.Visibility)

            typeText("zzqx no such item")                        ' nothing matches: the list stays open with a message
            Assert.Empty(visible())
            Assert.True(combo.IsDropDownOpen)
            Assert.Equal(Visibility.Visible, DirectCast(combo.Template.FindName("NoMatches", combo), FrameworkElement).Visibility)

            ' Typed text that isn't a picked item is rejected by the existing validation.
            GetType(StockAdjustmentWindow).GetMethod("Save_Click", BindingFlags.NonPublic Or BindingFlags.Instance).
                Invoke(adjust, {Nothing, Nothing})
            Assert.Equal("Select the item to adjust.", adjust.ItemError.Text)

            typeText("")                                         ' cleared: every item is offered again
            Assert.Equal(all.Count, visible().Count)

            typeText(fragment)                                   ' Enter with nothing highlighted picks the first match
            Dim first = visible().First()
            PressKey(box, Key.Enter)
            pump()
            Assert.Equal(first.Id, CInt(combo.SelectedValue))
            Assert.Equal(first.Name, combo.Text)
            Assert.False(combo.IsDropDownOpen)
            Assert.Equal(Visibility.Visible, adjust.CurrentPanel.Visibility)
            Assert.Equal(first.QuantityText, adjust.CurrentText.Text)
            Assert.Equal(all.Count, visible().Count)             ' after a pick, the next open lists everything

            combo.SelectedValue = target.Id                      ' a mouse pick: the name fills the field, the id is kept
            pump()
            Assert.Equal(target.Name, combo.Text)
            Assert.Equal(target.Id, CInt(combo.SelectedValue))
            Assert.Equal(target.PickerDetails, adjust.CurrentDetails.Text)

            typeText(target.Name.ToUpperInvariant())             ' an exact name typed, then leaving the field, selects it
            Assert.Null(combo.SelectedItem)
            adjust.AmountBox.Focus()
            pump()
            Assert.Equal(target.Id, CInt(combo.SelectedValue))
            Assert.Equal(target.Name, combo.Text)
        Finally
            adjust.Close()
        End Try

        ' Opened for a specific item: that item is picked and the full list is still available.
        Dim preselected = ShowOffScreen(New StockAdjustmentWindow(target.Id))
        Try
            Assert.Equal(target.Name, preselected.ItemBox.Text)
            Assert.Equal(target.Id, CInt(preselected.ItemBox.SelectedValue))
            Assert.Equal(all.Count, preselected.ItemBox.Items.Count)
        Finally
            preselected.Close()
        End Try

        ' Stock In uses the same picker; picking an item still fills in its supplier.
        Dim stockIn = ShowOffScreen(New StockInWindow(Nothing))
        Try
            Dim combo = stockIn.ItemBox
            Dim box = DirectCast(combo.Template.FindName("PART_EditableTextBox", combo), TextBox)
            stockIn.Activate()
            box.Focus()
            box.Text = fragment
            pump()
            Assert.All(combo.Items.Cast(Of Item)(), Sub(i) Assert.True(ItemSearchBox.Matches(i, fragment)))
            Assert.Null(combo.SelectedItem)
            Dim first = combo.Items.Cast(Of Item)().First()
            PressKey(box, Key.Enter)
            pump()
            Assert.Equal(first.Id, CInt(combo.SelectedValue))
            Assert.Equal(Visibility.Visible, stockIn.CurrentPanel.Visibility)
            Assert.Equal(first.SupplierId, CInt(stockIn.SupplierBox.SelectedValue))
        Finally
            stockIn.Close()
        End Try
    End Sub

    ''' <summary>Sends a key press to the focused element the way the keyboard would (preview first, then the bubbling event).</summary>
    Private Shared Sub PressKey(target As UIElement, pressed As Key)
        Dim source = PresentationSource.FromVisual(target)
        For Each routed In {Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent}
            Dim args As New KeyEventArgs(Keyboard.PrimaryDevice, source, 0, pressed) With {.RoutedEvent = routed}
            target.RaiseEvent(args)
            If args.Handled Then Exit For
        Next
    End Sub

    Private Shared Function ShowOffScreen(Of T As Window)(window As T) As T
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
