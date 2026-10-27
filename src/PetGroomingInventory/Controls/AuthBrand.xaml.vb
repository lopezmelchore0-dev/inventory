Class AuthBrand

    Private Sub AuthBrand_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ' Follow the shop's app name from Settings (the shell keeps it up to date).
        Dim name = MainWindow.Instance?.AppNameText.Text
        If Not String.IsNullOrWhiteSpace(name) Then AppNameText.Text = name
    End Sub

    Public Property Title As String
        Get
            Return TitleText.Text
        End Get
        Set(value As String)
            TitleText.Text = value
        End Set
    End Property

    Public Property Subtitle As String
        Get
            Return SubtitleText.Text
        End Get
        Set(value As String)
            SubtitleText.Text = value
        End Set
    End Property
End Class
