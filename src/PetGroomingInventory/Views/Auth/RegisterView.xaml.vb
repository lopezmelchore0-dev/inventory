Class RegisterView

    Private ReadOnly _errors As FormErrors
    Private _createdUsername As String

    Public Sub New()
        InitializeComponent()
        _errors = New FormErrors(GeneralError)
        _errors.Register("fullname", FullNameError, FullNameBox)
        _errors.Register("username", UsernameError, UsernameBox)
        _errors.Register("password", PasswordError, PasswordInput)
        _errors.Register("confirm", ConfirmError, ConfirmInput)
        _errors.Register("question", QuestionError, QuestionBox)
        _errors.Register("answer", AnswerError, AnswerBox)

        QuestionBox.ItemsSource = SecurityQuestions.All

        AuthUi.SubmitOnEnter(FormPanel, AddressOf Submit)
        AuthUi.CancelOnEscape(FormCard, AddressOf GoToLogin, CreateButton)
        AuthUi.MakeResponsive(Me, FormCard)
        AddHandler Loaded, Sub(s, e) FullNameBox.Focus()
    End Sub

    Private Sub UsernameBox_TextChanged(sender As Object, e As TextChangedEventArgs)
        PasswordInput.StrengthUsername = UsernameBox.Text
    End Sub

    Private Sub Create_Click(sender As Object, e As RoutedEventArgs)
        Submit()
    End Sub

    Private Async Sub Submit()
        If Not CreateButton.IsEnabled Then Return
        Dim input As New RegistrationInput With {
            .FullName = FullNameBox.Text.Trim(),
            .Username = UsernameBox.Text.Trim(),
            .Password = PasswordInput.Password,
            .ConfirmPassword = ConfirmInput.Password,
            .SecurityQuestion = TryCast(QuestionBox.SelectedItem, String),
            .SecurityAnswer = AnswerBox.Text
        }
        ' Check the form first so simple mistakes show instantly, without the hashing delay.
        _errors.Clear()
        Dim errors = AuthService.ValidateRegistration(input)
        If errors.Count > 0 Then
            _errors.Show(errors)
            Return
        End If

        Dim ok = Await AuthUi.RunAsync(CreateButton, "Creating account…", _errors, Sub() AuthService.Register(input))
        If Not ok Then Return

        _createdUsername = input.Username
        PasswordInput.Clear()
        ConfirmInput.Clear()
        AnswerBox.Clear()
        SuccessDetail.Text = $"You can now log in as ""{input.Username}"" with your new password."
        FormPanel.Visibility = Visibility.Collapsed
        SuccessPanel.Visibility = Visibility.Visible
        ProceedButton.Focus()
    End Sub

    Private Sub Proceed_Click(sender As Object, e As RoutedEventArgs)
        GoToLogin()
    End Sub

    Private Sub BackToLogin_Click(sender As Object, e As RoutedEventArgs)
        GoToLogin()
    End Sub

    ''' <summary>Back to Login; after a successful registration the new username is filled in.</summary>
    Private Sub GoToLogin()
        MainWindow.Instance?.ShowAuth(AuthScreen.Login, username:=_createdUsername)
    End Sub
End Class
