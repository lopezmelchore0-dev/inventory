Class RegisterView

    Private ReadOnly _errors As FormErrors
    Private ReadOnly _addedByAdmin As Boolean
    Private _createdUsername As String

    ''' <param name="addedByAdmin">True when a signed-in Admin adds another Admin from the Profile page.</param>
    Public Sub New(Optional addedByAdmin As Boolean = False)
        InitializeComponent()
        _addedByAdmin = addedByAdmin
        _errors = New FormErrors(GeneralError)
        _errors.Register("fullname", FullNameError, FullNameBox)
        _errors.Register("username", UsernameError, UsernameBox)
        _errors.Register("password", PasswordError, PasswordInput)
        _errors.Register("confirm", ConfirmError, ConfirmInput)
        _errors.Register("question", QuestionError, QuestionBox)
        _errors.Register("answer", AnswerError, AnswerBox)

        QuestionBox.ItemsSource = SecurityQuestions.All

        If addedByAdmin Then
            Brand.Title = "Add Admin Account"
            Brand.Subtitle = "Create another Admin account for this shop. Every account has full Admin access."
            LoginLinkPanel.Visibility = Visibility.Collapsed
            BackToProfileButton.Visibility = Visibility.Visible
            ProceedButton.Content = "Back to Profile"
        End If

        AuthUi.SubmitOnEnter(FormPanel, AddressOf Submit)
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

        Dim addedByAdmin = _addedByAdmin
        Dim ok = Await AuthUi.RunAsync(CreateButton, "Creating account…", _errors, Sub() AuthService.Register(input, addedByAdmin))
        If Not ok Then Return

        _createdUsername = input.Username
        PasswordInput.Clear()
        ConfirmInput.Clear()
        AnswerBox.Clear()
        SuccessDetail.Text = If(_addedByAdmin,
            $"""{input.Username}"" can now log in with their username and password.",
            $"You can now log in as ""{input.Username}"" with your new password.")
        FormPanel.Visibility = Visibility.Collapsed
        SuccessPanel.Visibility = Visibility.Visible
        ProceedButton.Focus()
        If _addedByAdmin Then MainWindow.Instance?.ShowToast($"Admin account ""{input.Username}"" was created.")
    End Sub

    Private Sub Proceed_Click(sender As Object, e As RoutedEventArgs)
        If _addedByAdmin Then
            MainWindow.Instance?.NavigateTo(PageKey.Profile)
        Else
            MainWindow.Instance?.ShowAuth(AuthScreen.Login, username:=_createdUsername)
        End If
    End Sub

    Private Sub BackToLogin_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance?.ShowAuth(AuthScreen.Login)
    End Sub

    Private Sub BackToProfile_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance?.NavigateTo(PageKey.Profile)
    End Sub
End Class
