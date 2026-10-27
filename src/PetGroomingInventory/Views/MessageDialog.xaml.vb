Public Enum MessageKind
    Info
    Success
    Warning
    [Error]
    Danger
End Enum

Class MessageDialog

    Public Sub New(title As String, message As String, kind As MessageKind, primaryText As String, secondaryText As String)
        InitializeComponent()
        Me.Title = title
        TitleText.Text = title
        MessageText.Text = message
        PrimaryButton.Content = primaryText

        If secondaryText Is Nothing Then
            SecondaryButton.Visibility = Visibility.Collapsed
            PrimaryButton.Margin = New Thickness(0)
            PrimaryButton.IsCancel = True
        Else
            SecondaryButton.Content = secondaryText
        End If

        Select Case kind
            Case MessageKind.Success
                IconGlyph.Text = ChrW(&HE73E)
            Case MessageKind.Warning
                IconGlyph.Text = ChrW(&HE7BA)
            Case MessageKind.Error
                IconGlyph.Text = ChrW(&HE783)
                IconCircle.Background = StatusPalette.Find("NeutralDarkBrush")
                IconGlyph.Foreground = Brushes.White
            Case MessageKind.Danger
                IconGlyph.Text = ChrW(&HE74D)
                IconCircle.Background = StatusPalette.Find("NeutralDarkBrush")
                IconGlyph.Foreground = Brushes.White
                PrimaryButton.Style = CType(FindResource("DarkButton"), Style)
            Case Else
                IconGlyph.Text = ChrW(&HE946)
        End Select
    End Sub

    Private Sub Primary_Click(sender As Object, e As RoutedEventArgs)
        DialogResult = True
    End Sub

    Private Sub Secondary_Click(sender As Object, e As RoutedEventArgs)
        DialogResult = False
    End Sub

    Private Sub Header_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        DragMove()
    End Sub
End Class
