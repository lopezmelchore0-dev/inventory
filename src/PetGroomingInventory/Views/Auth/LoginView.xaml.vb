Class LoginView

    Private ReadOnly _errors As FormErrors

    ''' <param name="registrationOpen">True only while no Admin account exists (first run).</param>
    ''' <param name="notice">Optional message shown above the form (e.g. after logout or a password reset).</param>
    Public Sub New(registrationOpen As Boolean, Optional notice As String = Nothing, Optional username As String = Nothing)
        InitializeComponent()
        _errors = New FormErrors(GeneralError)
        _errors.Register("username", UsernameError, UsernameBox)
        _errors.Register("password", PasswordError, PasswordInput)

        RegisterLinkPanel.Visibility = If(registrationOpen, Visibility.Visible, Visibility.Collapsed)
        RegisterClosedText.Visibility = If(registrationOpen, Visibility.Collapsed, Visibility.Visible)
        If Not String.IsNullOrWhiteSpace(notice) Then
            NoticeText.Text = notice
            NoticeBorder.Visibility = Visibility.Visible
        End If

        Dim remembered = RememberedLogin.Load()
        UsernameBox.Text = If(username, remembered)
        RememberBox.IsChecked = remembered IsNot Nothing

        AuthUi.SubmitOnEnter(FormCard, AddressOf Submit)
        AddHandler Loaded,
            Sub(s, e)
                If UsernameBox.Text.Length > 0 Then PasswordInput.Focus() Else UsernameBox.Focus()
            End Sub
    End Sub

    Private Sub Login_Click(sender As Object, e As RoutedEventArgs)
        Submit()
    End Sub

    Private Async Sub Submit()
        If Not LoginButton.IsEnabled Then Return
        Dim username = UsernameBox.Text.Trim()
        Dim password = PasswordInput.Password
        Dim remember = RememberBox.IsChecked = True
        NoticeBorder.Visibility = Visibility.Collapsed

        Dim ok = Await AuthUi.RunAsync(LoginButton, "Signing in…", _errors, Sub() AuthService.Login(username, password))
        If Not ok Then
            PasswordInput.Clear()
            If GeneralError.Visibility = Visibility.Visible Then PasswordInput.Focus()
            Return
        End If

        If remember Then RememberedLogin.Save(username) Else RememberedLogin.Clear()
        PasswordInput.Clear()
        MainWindow.Instance?.OnSignedIn()
    End Sub

    Private Sub Forgot_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance?.ShowAuth(AuthScreen.ForgotPassword)
    End Sub

    Private Sub Register_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance?.ShowAuth(AuthScreen.Register)
    End Sub
End Class
