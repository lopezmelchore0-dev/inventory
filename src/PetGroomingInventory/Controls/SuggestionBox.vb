''' <summary>
''' Turns an editable ComboBox into a free-text field with suggestions: the list opens when the field is
''' focused or clicked, narrows as the user types, and never limits what can be entered. Read the value from
''' ComboBox.Text (the typed text, or the picked suggestion).
''' </summary>
Public NotInheritable Class SuggestionBox
    Private ReadOnly _combo As ComboBox
    Private ReadOnly _view As ListCollectionView
    Private _filterText As String = ""
    Private _updating As Boolean

    Private Sub New(combo As ComboBox, suggestions As IEnumerable(Of String), maxLength As Integer)
        _combo = combo
        _view = New ListCollectionView(suggestions.ToList()) With {.Filter = AddressOf IsVisible}
        ' Set before the items arrive: a ComboBox synchronised with the view's current item selects the first suggestion by itself.
        combo.IsSynchronizedWithCurrentItem = False
        combo.IsEditable = True
        combo.IsTextSearchEnabled = False
        combo.ItemsSource = _view
        combo.SelectedIndex = -1

        ' ComboBox has no MaxLength of its own; limit the inner text box once the template exists.
        Dim limit =
            Sub()
                Dim box = TryCast(combo.Template?.FindName("PART_EditableTextBox", combo), TextBox)
                If box IsNot Nothing Then box.MaxLength = maxLength
            End Sub
        combo.ApplyTemplate()
        limit()
        AddHandler combo.Loaded, Sub(s, e) limit()

        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, New TextChangedEventHandler(AddressOf OnTextChanged))
        combo.AddHandler(UIElement.GotKeyboardFocusEvent, New KeyboardFocusChangedEventHandler(AddressOf OnGotFocus))
        combo.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, New MouseButtonEventHandler(AddressOf OnMouseDown))
    End Sub

    ''' <summary>Attaches the behaviour to <paramref name="combo"/> and returns the handle.</summary>
    Public Shared Function Attach(combo As ComboBox, suggestions As IEnumerable(Of String), maxLength As Integer) As SuggestionBox
        Return New SuggestionBox(combo, suggestions, maxLength)
    End Function

    ''' <summary>Suggestions currently shown (after filtering).</summary>
    Public ReadOnly Property VisibleSuggestions As IEnumerable(Of String)
        Get
            Return _view.Cast(Of String)()
        End Get
    End Property

    ''' <summary>A blank filter shows everything; otherwise a suggestion is shown when it contains the typed text.</summary>
    Public Shared Function Matches(suggestion As String, typed As String) As Boolean
        If String.IsNullOrWhiteSpace(typed) Then Return True
        Return suggestion.IndexOf(typed.Trim(), StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Private Function IsVisible(item As Object) As Boolean
        Return Matches(CStr(item), _filterText)
    End Function

    Private Sub OnTextChanged(sender As Object, e As TextChangedEventArgs)
        If _updating Then Return
        Dim box = TryCast(e.OriginalSource, TextBox)
        If box Is Nothing Then Return

        ' Only typing narrows the list. Text set by code (loading an item) or by picking a suggestion is not a
        ' search, so the full list is shown the next time the field opens.
        Dim picked = TryCast(_combo.SelectedItem, String)
        Dim isSearch = _combo.IsKeyboardFocusWithin AndAlso Not (picked IsNot Nothing AndAlso String.Equals(picked, box.Text, StringComparison.Ordinal))
        Dim newFilter = If(isSearch, box.Text.Trim(), "")

        If newFilter <> _filterText Then
            _filterText = newFilter
            ' Refreshing the view can reset an editable ComboBox's text, so put the text and caret back.
            Dim text = box.Text
            Dim caret = box.SelectionStart
            _updating = True
            Try
                _view.Refresh()
                If box.Text <> text Then box.Text = text
                box.Select(Math.Min(caret, text.Length), 0)
            Finally
                _updating = False
            End Try
        End If

        If Not isSearch Then Return
        Dim shouldOpen = _view.Count > 0
        If _combo.IsDropDownOpen <> shouldOpen Then
            _combo.IsDropDownOpen = shouldOpen
            ' Opening the list selects the text; move the caret back so typing continues instead of replacing it.
            _combo.Dispatcher.BeginInvoke(Sub() box.Select(box.Text.Length, 0), Threading.DispatcherPriority.Background)
        End If
    End Sub

    Private Sub OnGotFocus(sender As Object, e As KeyboardFocusChangedEventArgs)
        If TypeOf e.OriginalSource Is TextBox AndAlso _view.Count > 0 Then _combo.IsDropDownOpen = True
    End Sub

    ' Clicking the text area of an already-focused field opens the list too (the arrow button handles itself).
    Private Sub OnMouseDown(sender As Object, e As MouseButtonEventArgs)
        If _combo.IsDropDownOpen OrElse _view.Count = 0 Then Return
        Dim source = TryCast(e.OriginalSource, DependencyObject)
        While source IsNot Nothing AndAlso source IsNot _combo
            If TypeOf source Is System.Windows.Controls.Primitives.ToggleButton Then Return
            source = If(TypeOf source Is Visual, VisualTreeHelper.GetParent(source), LogicalTreeHelper.GetParent(source))
        End While
        _combo.IsDropDownOpen = True
    End Sub
End Class
