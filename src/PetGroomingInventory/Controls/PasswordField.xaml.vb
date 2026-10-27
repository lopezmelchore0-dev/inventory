''' <summary>
''' Password input with a show/hide toggle and an optional strength meter + requirements checklist.
''' Works with FormErrors: setting BorderBrush highlights the box and Focus() moves into it.
''' </summary>
Class PasswordField

    ' Weak → Strong, light pink → dark pink.
    Private Shared ReadOnly StrengthBrushKeys As String() = {"InputHoverBorderBrush", "PrimaryBrush", "AccentButtonBrush", "PrimaryDarkBrush"}

    Private _syncing As Boolean
    Private _revealed As Boolean
    Private _username As String

    Public Event PasswordChanged As EventHandler

    Public Sub New()
        InitializeComponent()
        AddHandler GotKeyboardFocus, AddressOf ForwardFocus
        UpdatePlaceholder()
    End Sub

    Public Property Placeholder As String
        Get
            Return PlaceholderText.Text
        End Get
        Set(value As String)
            PlaceholderText.Text = value
            VisibleBox.Tag = value
        End Set
    End Property

    ''' <summary>Shows the strength meter and requirements checklist (for new passwords).</summary>
    Public Property ShowStrength As Boolean
        Get
            Return StrengthPanel.Visibility = Visibility.Visible
        End Get
        Set(value As Boolean)
            StrengthPanel.Visibility = If(value, Visibility.Visible, Visibility.Collapsed)
            UpdateStrength()
        End Set
    End Property

    ''' <summary>Username used by the "does not contain your username" rule.</summary>
    Public Property StrengthUsername As String
        Get
            Return _username
        End Get
        Set(value As String)
            _username = value
            UpdateStrength()
        End Set
    End Property

    Public Property Password As String
        Get
            Return If(_revealed, VisibleBox.Text, HiddenBox.Password)
        End Get
        Set(value As String)
            _syncing = True
            HiddenBox.Password = If(value, "")
            VisibleBox.Text = If(value, "")
            _syncing = False
            OnPasswordChanged()
        End Set
    End Property

    Public Sub Clear()
        SetRevealed(False)
        Password = ""
    End Sub

    Private Sub HiddenBox_PasswordChanged(sender As Object, e As RoutedEventArgs)
        If _syncing OrElse _revealed Then Return
        OnPasswordChanged()
    End Sub

    Private Sub VisibleBox_TextChanged(sender As Object, e As TextChangedEventArgs)
        If _syncing OrElse Not _revealed Then Return
        OnPasswordChanged()
    End Sub

    Private Sub OnPasswordChanged()
        UpdatePlaceholder()
        UpdateStrength()
        RaiseEvent PasswordChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub Reveal_Click(sender As Object, e As RoutedEventArgs)
        SetRevealed(Not _revealed)
        ActiveBox.Focus()
    End Sub

    Private Sub SetRevealed(reveal As Boolean)
        If reveal = _revealed Then Return
        Dim current = Password
        _syncing = True
        If reveal Then
            VisibleBox.Text = current
            VisibleBox.CaretIndex = current.Length
        Else
            HiddenBox.Password = current
            VisibleBox.Clear()
        End If
        _syncing = False
        _revealed = reveal
        HiddenBox.Visibility = If(reveal, Visibility.Collapsed, Visibility.Visible)
        VisibleBox.Visibility = If(reveal, Visibility.Visible, Visibility.Collapsed)
        RevealButton.Content = If(reveal, ChrW(&HED1A), ChrW(&HE7B3))
        RevealButton.ToolTip = If(reveal, "Hide password", "Show password")
        UpdatePlaceholder()
    End Sub

    Private ReadOnly Property ActiveBox As Control
        Get
            Return If(_revealed, CType(VisibleBox, Control), HiddenBox)
        End Get
    End Property

    Private Sub UpdatePlaceholder()
        ' The TextBox shows its own placeholder through Tag.
        PlaceholderText.Visibility = If(Not _revealed AndAlso HiddenBox.Password.Length = 0, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Sub UpdateStrength()
        If StrengthPanel.Visibility <> Visibility.Visible Then Return
        Dim pwd = Password
        Dim score = PasswordPolicy.Score(pwd, _username)
        Dim fill = StatusPalette.Find(StrengthBrushKeys(Math.Max(score, 1) - 1))
        Dim empty = StatusPalette.Find("LavenderBrush")
        Dim segments = {Segment1, Segment2, Segment3, Segment4}
        For i = 0 To segments.Length - 1
            segments(i).Background = If(i < score, fill, empty)
        Next
        StrengthLabel.Text = If(score = 0, "—", PasswordPolicy.ScoreLabel(score))
        StrengthLabel.Foreground = If(score = 0, StatusPalette.Find("TextMutedBrush"), If(score >= 3, StatusPalette.Find("PrimaryDarkBrush"), StatusPalette.Find("TextSecondaryBrush")))
        RulesList.ItemsSource = PasswordPolicy.Evaluate(pwd, _username)
    End Sub

    ' ---- FormErrors integration ----

    Private Sub ForwardFocus(sender As Object, e As KeyboardFocusChangedEventArgs)
        If e.NewFocus Is Me Then ActiveBox.Focus()
    End Sub

    Protected Overrides Sub OnPropertyChanged(e As DependencyPropertyChangedEventArgs)
        MyBase.OnPropertyChanged(e)
        If e.Property Is BorderBrushProperty AndAlso HiddenBox IsNot Nothing Then
            If ReadLocalValue(BorderBrushProperty) Is DependencyProperty.UnsetValue Then
                HiddenBox.ClearValue(BorderBrushProperty)
                VisibleBox.ClearValue(BorderBrushProperty)
            Else
                HiddenBox.BorderBrush = BorderBrush
                VisibleBox.BorderBrush = BorderBrush
            End If
        End If
    End Sub
End Class
