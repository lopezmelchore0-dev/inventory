''' <summary>
''' The Unit field shared by Add / Edit Item, Stock In and Stock Adjustment: an editable ComboBox offering the predefined
''' units, "Others" (type your own) and custom units already used by items. Every form gets the same options, the same
''' "Others" prompt and the same whole-number hint. Read the value with <see cref="Value"/> (the unit to store).
''' </summary>
Public NotInheritable Class UnitField
    Private ReadOnly _box As ComboBox
    Private ReadOnly _hint As TextBlock
    Private ReadOnly _changed As Action
    Private _customUnitRequested As Boolean

    Public Const CustomUnitPrompt As String = "Type the unit, e.g. tray, carton or sheet."

    Private Sub New(box As ComboBox, hint As TextBlock, changed As Action)
        _box = box
        _hint = hint
        _changed = changed
        SuggestionBox.Attach(box, InventoryService.GetUnitSuggestions(), MeasurementUnits.MaxLength)
        box.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, New TextChangedEventHandler(AddressOf OnTextChanged))
        AddHandler box.SelectionChanged, AddressOf OnSelectionChanged
    End Sub

    ''' <summary>Attaches the Unit field behaviour. <paramref name="changed"/> runs whenever the unit text changes.</summary>
    Public Shared Function Attach(box As ComboBox, hint As TextBlock, Optional changed As Action = Nothing) As UnitField
        Return New UnitField(box, hint, changed)
    End Function

    ''' <summary>The unit to store for what is in the box (see MeasurementUnits.Normalize); Nothing when none is chosen.</summary>
    Public ReadOnly Property Value As String
        Get
            Return MeasurementUnits.Normalize(_box.Text)
        End Get
    End Property

    ''' <summary>Shows a stored unit in the box as its dropdown label (blank for Nothing).</summary>
    Public Sub Show(unit As String)
        _customUnitRequested = False
        _box.SelectedIndex = -1
        _box.Text = MeasurementUnits.LabelFor(unit)
        UpdateHint()
    End Sub

    ''' <summary>"Others" is a prompt, not a unit: clear it so the user types their own.</summary>
    Private Sub OnSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If Not String.Equals(TryCast(_box.SelectedItem, String), MeasurementUnits.OtherLabel, StringComparison.Ordinal) Then Return
        ' The ComboBox writes the picked text after this event, so clear it once that has happened.
        _box.Dispatcher.BeginInvoke(
            Sub()
                _box.SelectedIndex = -1
                _box.Text = ""
                _box.IsDropDownOpen = False
                _box.Focus()
                _customUnitRequested = True
                UpdateHint()
            End Sub, Threading.DispatcherPriority.Background)
    End Sub

    Private Sub OnTextChanged(sender As Object, e As TextChangedEventArgs)
        UpdateHint()
        _changed?.Invoke()
    End Sub

    Private Sub UpdateHint()
        Dim unit = Value
        If unit Is Nothing AndAlso _customUnitRequested Then
            _hint.Text = CustomUnitPrompt
        ElseIf MeasurementUnits.IsCountable(unit) Then
            _hint.Text = $"Counted in whole {MeasurementUnits.Display(unit, 2D)}: stock in and adjustments accept whole numbers only."
        Else
            _hint.Text = ""
        End If
        _hint.Visibility = If(_hint.Text.Length > 0, Visibility.Visible, Visibility.Collapsed)
    End Sub

    ''' <summary>
    ''' Stock In / Stock Adjustment: shows (or clears) the problem with the chosen unit as it is picked, so a unit other
    ''' than the item's registered one is flagged before saving.
    ''' </summary>
    Public Shared Sub ShowProblem(box As ComboBox, errorLabel As TextBlock, message As String)
        errorLabel.Text = If(message, "")
        errorLabel.Visibility = If(message Is Nothing, Visibility.Collapsed, Visibility.Visible)
        If message Is Nothing Then
            box.ClearValue(Control.BorderBrushProperty)
        Else
            box.BorderBrush = StatusPalette.Find("ErrorTextBrush")
        End If
    End Sub
End Class
