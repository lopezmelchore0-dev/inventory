''' <summary>
''' Turns an editable ComboBox (style ItemSearchComboBox) into a searchable item picker: the list opens when the
''' field is focused or clicked, narrows to matching items as the user types, and only a picked item counts.
''' Read the choice from ComboBox.SelectedItem / SelectedValue; typed text alone never selects anything.
''' </summary>
Public NotInheritable Class ItemSearchBox
    Private ReadOnly _combo As ComboBox
    Private ReadOnly _items As List(Of Item)
    Private ReadOnly _view As ListCollectionView
    Private _filterText As String = ""
    Private _updating As Boolean

    Private Sub New(combo As ComboBox, items As IEnumerable(Of Item))
        _combo = combo
        _items = items.ToList()
        _view = New ListCollectionView(_items) With {.Filter = AddressOf IsVisible}
        ' Set before the items arrive: a ComboBox synchronised with the view's current item selects the first item by itself.
        combo.IsSynchronizedWithCurrentItem = False
        combo.IsEditable = True
        combo.IsTextSearchEnabled = False
        TextSearch.SetTextPath(combo, NameOf(Item.Name))   ' text shown once an item is picked
        combo.ItemsSource = _view
        combo.SelectedIndex = -1

        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, New TextChangedEventHandler(AddressOf OnTextChanged))
        combo.AddHandler(UIElement.GotKeyboardFocusEvent, New KeyboardFocusChangedEventHandler(AddressOf OnGotFocus))
        combo.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, New MouseButtonEventHandler(AddressOf OnMouseDown))
        combo.AddHandler(UIElement.PreviewKeyDownEvent, New KeyEventHandler(AddressOf OnPreviewKeyDown), handledEventsToo:=True)
        AddHandler combo.DropDownClosed, AddressOf OnDropDownClosed
        AddHandler combo.IsKeyboardFocusWithinChanged, AddressOf OnFocusWithinChanged
    End Sub

    ''' <summary>Attaches the behaviour to <paramref name="combo"/> and returns the handle.</summary>
    Public Shared Function Attach(combo As ComboBox, items As IEnumerable(Of Item)) As ItemSearchBox
        Return New ItemSearchBox(combo, items)
    End Function

    ''' <summary>Items currently shown (after filtering).</summary>
    Public ReadOnly Property VisibleItems As IEnumerable(Of Item)
        Get
            Return _view.Cast(Of Item)()
        End Get
    End Property

    ''' <summary>
    ''' A blank search shows every item. Otherwise each typed word must appear (ignoring case) in the item's name,
    ''' description (size or variant) or category, so "dog oat" finds "Oatmeal Dog Shampoo".
    ''' </summary>
    Public Shared Function Matches(item As Item, typed As String) As Boolean
        If String.IsNullOrWhiteSpace(typed) Then Return True
        Dim fields = {item.Name, item.Description, item.CategoryName}
        Return typed.Split(CType(Nothing, Char()), StringSplitOptions.RemoveEmptyEntries).
            All(Function(word) fields.Any(Function(f) f IsNot Nothing AndAlso f.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
    End Function

    Private Function IsVisible(item As Object) As Boolean
        Return Matches(DirectCast(item, Item), _filterText)
    End Function

    Private ReadOnly Property EditBox As TextBox
        Get
            Return TryCast(_combo.Template?.FindName("PART_EditableTextBox", _combo), TextBox)
        End Get
    End Property

    ''' <summary>The item under the mouse or keyboard highlight in the open list, if any.</summary>
    Private Function HighlightedItem() As Item
        For Each obj In _view
            Dim container = TryCast(_combo.ItemContainerGenerator.ContainerFromItem(obj), ComboBoxItem)
            If container IsNot Nothing AndAlso container.IsHighlighted Then Return DirectCast(obj, Item)
        Next
        Return Nothing
    End Function

    ''' <summary>Applies a new filter and/or clears the selection while keeping the typed text and caret where they were.</summary>
    Private Sub Update(box As TextBox, newFilter As String, clearSelection As Boolean)
        Dim text = box.Text
        Dim caret = box.SelectionStart
        _updating = True
        Try
            If clearSelection Then _combo.SelectedItem = Nothing
            If newFilter <> _filterText Then
                _filterText = newFilter
                _view.Refresh()
            End If
            ' Clearing the selection or refreshing the view can reset an editable ComboBox's text, so put it back.
            If box.Text <> text Then box.Text = text
            box.Select(Math.Min(caret, text.Length), 0)
        Finally
            _updating = False
        End Try
    End Sub

    Private Sub OnTextChanged(sender As Object, e As TextChangedEventArgs)
        If _updating Then Return
        Dim box = TryCast(e.OriginalSource, TextBox)
        If box Is Nothing OrElse Not _combo.IsKeyboardFocusWithin Then Return   ' text set by code (preselecting an item)

        ' Picking an item, or moving through the list with the arrow keys, shows that item's name: not a search.
        Dim picked = TryCast(_combo.SelectedItem, Item)
        If picked IsNot Nothing AndAlso String.Equals(picked.Name, box.Text, StringComparison.Ordinal) Then Return
        Dim highlighted = HighlightedItem()
        If highlighted IsNot Nothing AndAlso String.Equals(highlighted.Name, box.Text, StringComparison.Ordinal) Then Return

        ' The user typed: search, and drop any earlier pick so its id can't be submitted under different text.
        Update(box, box.Text.Trim(), picked IsNot Nothing)
        If Not _combo.IsDropDownOpen Then
            _combo.IsDropDownOpen = True   ' stays open with no matches to show "No matching items found"
            ' Opening the list selects the text; move the caret back so typing continues instead of replacing it.
            _combo.Dispatcher.BeginInvoke(Sub() box.Select(box.Text.Length, 0), Threading.DispatcherPriority.Background)
        End If
    End Sub

    Private Sub OnGotFocus(sender As Object, e As KeyboardFocusChangedEventArgs)
        Dim box = TryCast(e.OriginalSource, TextBox)
        If box Is Nothing Then Return
        _combo.IsDropDownOpen = True
        box.SelectAll()   ' typing replaces the current item's name with a new search
    End Sub

    ' Clicking the text area of an already-focused field opens the list too (the arrow button handles itself).
    Private Sub OnMouseDown(sender As Object, e As MouseButtonEventArgs)
        If _combo.IsDropDownOpen Then Return
        Dim source = TryCast(e.OriginalSource, DependencyObject)
        While source IsNot Nothing AndAlso source IsNot _combo
            If TypeOf source Is System.Windows.Controls.Primitives.ToggleButton Then Return
            source = If(TypeOf source Is Visual, VisualTreeHelper.GetParent(source), LogicalTreeHelper.GetParent(source))
        End While
        _combo.IsDropDownOpen = True
    End Sub

    ' An editable ComboBox handles Enter itself before any instance handler: it closes the open list and picks the
    ' highlighted item. When nothing was highlighted it picks nothing, so pick the first match of the search instead.
    Private Sub OnPreviewKeyDown(sender As Object, e As KeyEventArgs)
        If e.Key <> Key.Enter OrElse Not e.Handled OrElse _combo.SelectedItem IsNot Nothing Then Return
        If _filterText = "" OrElse _view.IsEmpty Then Return
        _combo.SelectedItem = _view.GetItemAt(0)
        Dim box = EditBox
        If box Is Nothing Then Return
        Update(box, "", clearSelection:=False)   ' the list is already closed, so reset it here for the next open
        box.Select(box.Text.Length, 0)
    End Sub

    ' Once an item is picked, the next time the list opens it shows every item again.
    Private Sub OnDropDownClosed(sender As Object, e As EventArgs)
        Dim box = EditBox
        If box Is Nothing OrElse _combo.SelectedItem Is Nothing OrElse _filterText = "" Then Return
        Update(box, "", clearSelection:=False)
    End Sub

    ' Leaving the field with an item's exact name typed (but not picked) selects that item.
    Private Sub OnFocusWithinChanged(sender As Object, e As DependencyPropertyChangedEventArgs)
        If _combo.IsKeyboardFocusWithin OrElse _combo.SelectedItem IsNot Nothing Then Return
        Dim typed = _combo.Text.Trim()
        If typed = "" Then Return
        Dim exact = _items.Where(Function(i) String.Equals(i.Name.Trim(), typed, StringComparison.OrdinalIgnoreCase)).Take(2).ToList()
        If exact.Count <> 1 Then Return
        _filterText = ""
        _view.Refresh()
        _combo.SelectedItem = exact(0)
    End Sub
End Class
