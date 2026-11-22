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
        CheckAuthResponsiveness()
        CheckAuthKeyboard(shell)

        AuthService.Login("maria", "Grooming#2026")
        shell.OnSignedIn()
        Assert.IsType(Of DashboardView)(shell.PageHost.Content)
        Assert.Equal(Visibility.Visible, shell.ShellRoot.Visibility)
        Assert.Equal(Visibility.Collapsed, shell.AuthPanel.Visibility)
        Assert.Equal("Maria Santos", shell.AccountNameText.Text)
        For Each key In {PageKey.Profile, PageKey.ChangePassword, PageKey.Settings, PageKey.Reports, PageKey.Help}
            Assert.NotNull(shell.NavigateTo(key))
            Layout(shell)
        Next
        CheckHelp(DirectCast(shell.PageHost.Content, HelpView))

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

        ' The Unit field starts at the preselected item's registered unit, shown as its dropdown label.
        Dim expectedUnit = MeasurementUnits.LabelFor(item.Unit)
        Assert.Equal("", DirectCast(dialogs(4), StockInWindow).UnitBox.Text)
        Assert.Equal(expectedUnit, DirectCast(dialogs(5), StockInWindow).UnitBox.Text)
        Assert.Equal(expectedUnit, DirectCast(dialogs(6), StockAdjustmentWindow).UnitBox.Text)
        Assert.Equal(expectedUnit, DirectCast(dialogs(1), ItemEditorWindow).UnitBox.Text)

        CheckUnitFields()
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

    ''' <summary>Help &amp; Support opens on the preview, switches to the full guide (at a topic) and back.</summary>
    Private Shared Sub CheckHelp(view As HelpView)
        Assert.Equal(Visibility.Visible, view.PreviewPanel.Visibility)
        Assert.False(view.IsGuideOpen)
        Assert.Equal(HelpContent.Topics.Count, view.TopicList.Items.Count)

        view.ShowGuide(HelpContent.Topics(4))
        Layout(view)
        Assert.True(view.IsGuideOpen)
        Assert.Equal(Visibility.Collapsed, view.PreviewPanel.Visibility)
        Assert.Equal(HelpContent.Topics.Count, view.GuideSections.Items.Count)

        view.ShowPreview()
        Layout(view)
        Assert.False(view.IsGuideOpen)
        Assert.Equal(Visibility.Visible, view.PreviewPanel.Visibility)
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

    ''' <summary>
    ''' Add Item, Stock In and Stock Adjustment share one Unit field: the same editable dropdown, options, "Others" prompt
    ''' and hint. In the stock forms it starts at the item's registered unit and flags any other unit at once.
    ''' Built with layout only: no window is shown or focused.
    ''' </summary>
    Private Shared Sub CheckUnitFields()
        Dim pump = Sub() Dispatcher.CurrentDispatcher.Invoke(Sub() Exit Sub, DispatcherPriority.Background)
        Dim item = InventoryService.GetItems().First(Function(i) MeasurementUnits.IsPredefined(i.Unit))
        Dim registered = MeasurementUnits.LabelFor(item.Unit)
        Dim otherUnit = MeasurementUnits.Predefined.First(Function(u) u.Value <> item.Unit).Label

        Dim add As New ItemEditorWindow(Nothing)
        Dim stockIn As New StockInWindow(item.Id)
        Dim adjust As New StockAdjustmentWindow(item.Id)
        For Each w As Window In New Window() {add, stockIn, adjust}
            Layout(w)
        Next
        Dim fields = {(Box:=add.UnitBox, Hint:=add.UnitHint, [Error]:=add.UnitError),
                      (Box:=stockIn.UnitBox, Hint:=stockIn.UnitHint, [Error]:=stockIn.UnitError),
                      (Box:=adjust.UnitBox, Hint:=adjust.UnitHint, [Error]:=adjust.UnitError)}
        Dim options = Function(c As ComboBox) c.Items.Cast(Of String)().ToList()

        ' Identical design and options in all three forms, starting with the 17 required entries ("Others" last).
        Dim reference = options(add.UnitBox)
        Assert.Equal(MeasurementUnits.Labels, reference.Take(17))
        Assert.Equal("Others", reference(16))
        For Each f In fields
            Assert.True(f.Box.IsEditable)
            Assert.Same(add.UnitBox.Style, f.Box.Style)
            Assert.Equal(add.UnitBox.Width, f.Box.Width)
            Assert.Equal(reference, options(f.Box))
            Assert.Equal(MeasurementUnits.MaxLength, DirectCast(f.Box.Template.FindName("PART_EditableTextBox", f.Box), TextBox).MaxLength)
        Next

        ' Add Item starts blank; the stock forms start at the item's registered unit with no error.
        Assert.Equal("", add.UnitBox.Text)
        Assert.Equal(registered, stockIn.UnitBox.Text)
        Assert.Equal(registered, adjust.UnitBox.Text)
        Assert.Equal(Visibility.Collapsed, stockIn.UnitError.Visibility)

        ' "Others" clears the box and asks for a custom unit, in every form.
        For Each f In fields
            f.Box.SelectedItem = MeasurementUnits.OtherLabel
            pump()
            Assert.Equal("", f.Box.Text)
            Assert.Equal(UnitField.CustomUnitPrompt, f.Hint.Text)
            Assert.Equal(Visibility.Collapsed, f.Error.Visibility)   ' a blank unit is only reported on save
        Next

        ' A counted unit shows the whole-number hint, in every form.
        For Each f In fields
            f.Box.Text = "Bottle"
            Assert.Equal("Counted in whole bottles: stock in and adjustments accept whole numbers only.", f.Hint.Text)
        Next

        ' The stock forms flag any unit other than the item's registered one, and accept it again once it is restored.
        For Each f In fields.Skip(1)
            f.Box.Text = otherUnit
            Assert.Equal(Visibility.Visible, f.Error.Visibility)
            Assert.Contains($"registered in ""{registered}""", f.Error.Text)
            f.Box.Text = registered
            Assert.Equal(Visibility.Collapsed, f.Error.Visibility)
        Next

        ' Switching the adjustment type or typing an amount keeps the chosen unit.
        adjust.UnitBox.Text = otherUnit
        adjust.ModeAdd.IsChecked = True
        adjust.AmountBox.Text = "2"
        Assert.Equal(otherUnit, adjust.UnitBox.Text)

        ' Choosing another item resets the field to that item's unit.
        Dim second = InventoryService.GetItems().First(Function(i) i.Id <> item.Id AndAlso MeasurementUnits.IsPredefined(i.Unit))
        adjust.ItemBox.SelectedValue = second.Id
        Assert.Equal(MeasurementUnits.LabelFor(second.Unit), adjust.UnitBox.Text)
        Assert.Equal(Visibility.Collapsed, adjust.UnitError.Visibility)
        stockIn.ItemBox.SelectedValue = second.Id
        Assert.Equal(MeasurementUnits.LabelFor(second.Unit), stockIn.UnitBox.Text)
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
    Private Shared Sub Layout(element As FrameworkElement, Optional width As Double = 1400, Optional height As Double = 900)
        element.ApplyTemplate()
        Dim target As FrameworkElement = element
        If TypeOf element Is Window Then target = DirectCast(DirectCast(element, Window).Content, FrameworkElement)
        target.Measure(New Size(width, height))
        target.Arrange(New Rect(0, 0, width, height))
        target.UpdateLayout()
    End Sub

    ' Viewport sizes from the auth UX brief. In WPF these are window-content sizes in DIPs, which occur on
    ' small/portrait screens and with display scaling (e.g. 1366×768 at 150% ≈ 911×512).
    Private Shared ReadOnly AuthViewports As Size() = {
        New Size(320, 568), New Size(375, 667), New Size(390, 844), New Size(430, 932), New Size(768, 1024),
        New Size(1024, 768), New Size(1366, 768), New Size(1440, 900), New Size(1920, 1080)}

    ''' <summary>
    ''' Auth screens fit every viewport without horizontal clipping, scroll vertically when too tall,
    ''' and keep their original card widths on a normal desktop window.
    ''' </summary>
    Private Shared Sub CheckAuthResponsiveness()
        Dim builders As (Name As String, Create As Func(Of FrameworkElement), DesktopWidth As Double)() = {
            ("Login", Function() New LoginView(True, "A long notice that has to wrap onto several lines when the window is narrow.", "maria"), 460),
            ("Login (closed)", Function() New LoginView(False), 460),
            ("Register", Function() New RegisterView(), 500),
            ("Forgot Password", Function() New ForgotPasswordView(), 480),
            ("Change Password", Function() New ChangePasswordView(), 560)}

        For Each b In builders
            For Each size In AuthViewports
                Dim view = b.Create()
                ' Twice: the first pass raises SizeChanged, which may switch the card to compact padding.
                Layout(view, size.Width, size.Height)
                Layout(view, size.Width, size.Height)
                Dim where = $"{b.Name} at {size.Width}×{size.Height}"

                Dim overflow = FindHorizontalOverflow(view, view, size.Width)
                Assert.True(overflow Is Nothing, $"{where}: {overflow} extends past the right edge.")

                Dim scroller = DirectCast(DirectCast(view, UserControl).Content, ScrollViewer)
                If scroller.ExtentHeight > scroller.ViewportHeight + 1 Then
                    Assert.True(scroller.ComputedVerticalScrollBarVisibility = Visibility.Visible, $"{where}: content is taller than the window but can't scroll.")
                End If

                If size = New Size(1440, 900) Then
                    Dim cardWidth = If(TypeOf view Is ChangePasswordView,
                                       DirectCast(scroller.Content, Grid).ColumnDefinitions(0).ActualWidth,
                                       FindCard(view).ActualWidth)
                    Assert.Equal(b.DesktopWidth, cardWidth, 1)
                End If
            Next
        Next
    End Sub

    Private Shared Function FindCard(view As FrameworkElement) As Border
        Return DirectCast(view.FindName("FormCard"), Border)
    End Function

    ''' <summary>Returns a description of the first visible element whose right edge lies beyond <paramref name="width"/>.</summary>
    Private Shared Function FindHorizontalOverflow(root As FrameworkElement, element As DependencyObject, width As Double) As String
        Dim fe = TryCast(element, FrameworkElement)
        If fe IsNot Nothing Then
            If fe.Visibility <> Visibility.Visible Then Return Nothing
            If fe IsNot root AndAlso fe.ActualWidth > 0 Then
                Dim bounds = fe.TransformToAncestor(root).TransformBounds(New Rect(fe.RenderSize))
                If bounds.Right > width + 0.5 OrElse bounds.Left < -0.5 Then
                    Return $"{fe.GetType().Name} '{fe.Name}' ({bounds.Left:0}–{bounds.Right:0})"
                End If
            End If
            ' Inputs scroll their own text internally; their template parts are not page layout.
            If TypeOf fe Is Primitives.TextBoxBase OrElse TypeOf fe Is PasswordBox OrElse TypeOf fe Is ComboBox Then Return Nothing
        End If
        For i = 0 To Media.VisualTreeHelper.GetChildrenCount(element) - 1
            Dim found = FindHorizontalOverflow(root, Media.VisualTreeHelper.GetChild(element, i), width)
            If found IsNot Nothing Then Return found
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' Enter on a check box such as "Remember me" submits the form instead of toggling it; Esc leaves
    ''' Forgot Password and Register. Nothing here pumps the dispatcher: that would also run the
    ''' Application's queued startup (StartupUri) and open a real window.
    ''' </summary>
    Private Shared Sub CheckAuthKeyboard(shell As MainWindow)
        Dim remember As New CheckBox With {.Content = "Remember me"}
        Dim form As New StackPanel()
        form.Children.Add(New TextBox())
        form.Children.Add(remember)
        Dim submitted = 0
        AuthUi.SubmitOnEnter(form, Sub() submitted += 1)
        Dim host = ShowOffScreen(New Window With {.Content = form, .Width = 400, .Height = 300})
        Try
            remember.Focus()
            PressKey(remember, Key.Enter)
            Assert.Equal(1, submitted)
            Assert.False(remember.IsChecked)                   ' not toggled
        Finally
            host.Close()
        End Try

        For Each create In New Func(Of UserControl)() {Function() New ForgotPasswordView(), Function() New RegisterView()}
            Dim view = create()
            host = ShowOffScreen(New Window With {.Content = view, .Width = 500, .Height = 700})
            Try
                Dim box = DirectCast(view.FindName(If(TypeOf view Is RegisterView, "FullNameBox", "UsernameBox")), TextBox)
                box.Focus()
                shell.ShowAuth(AuthScreen.ForgotPassword)
                PressKey(box, Key.Escape)
                Assert.True(TypeOf shell.AuthPanel.Content Is LoginView, $"Esc on {view.GetType().Name} should return to Login.")
            Finally
                host.Close()
            End Try
        Next
    End Sub
End Class
