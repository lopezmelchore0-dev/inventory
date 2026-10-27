Class ProfileView

    Private ReadOnly _errors As FormErrors

    Public Sub New()
        InitializeComponent()
        _errors = New FormErrors(GeneralError)
        _errors.Register("fullname", FullNameError, FullNameBox)
        _errors.Register("username", UsernameError, UsernameBox)
        _errors.Register("question", QuestionError, QuestionBox)
        _errors.Register("answer", AnswerError, AnswerBox)
        _errors.Register("current", CurrentPasswordError, CurrentPasswordInput)
        QuestionBox.ItemsSource = SecurityQuestions.All
        AuthUi.SubmitOnEnter(EditForm, AddressOf Save)
        ' Never leave a typed password or answer behind when the Admin moves to another page.
        AddHandler IsVisibleChanged,
            Sub(s, e)
                If Not IsVisible Then ClearSecrets()
            End Sub
    End Sub

    Protected Overrides Sub LoadData()
        Dim account = AuthService.GetCurrentAccount()
        InitialsText.Text = account.Initials
        FullNameText.Text = account.FullName
        UsernameText.Text = "@" & account.Username
        SummaryFullName.Text = account.FullName
        SummaryUsername.Text = account.Username
        RoleText.Text = account.Role
        LastLoginText.Text = If(account.LastLoginAt.HasValue, Fmt.DateTimeText(account.LastLoginAt.Value), "—")
        PasswordChangedText.Text = $"Password last changed {Fmt.DateTimeText(account.PasswordChangedAt)}."
        FillForm(account)
    End Sub

    Private Sub FillForm(account As AdminAccount)
        _errors.Clear()
        FullNameBox.Text = account.FullName
        UsernameBox.Text = account.Username
        QuestionBox.SelectedItem = SecurityQuestions.All.FirstOrDefault(Function(q) q = account.SecurityQuestion)
        ClearSecrets()
    End Sub

    Private Sub ClearSecrets()
        AnswerBox.Clear()
        CurrentPasswordInput.Clear()
    End Sub

    Private Sub Save_Click(sender As Object, e As RoutedEventArgs)
        Save()
    End Sub

    Private Async Sub Save()
        If Not SaveButton.IsEnabled Then Return
        Dim oldUsername = AuthSession.Username
        Dim input As New ProfileInput With {
            .FullName = FullNameBox.Text.Trim(),
            .Username = UsernameBox.Text.Trim(),
            .SecurityQuestion = TryCast(QuestionBox.SelectedItem, String),
            .NewSecurityAnswer = AnswerBox.Text,
            .CurrentPassword = CurrentPasswordInput.Password
        }
        Dim changed As Boolean
        Dim ok = Await AuthUi.RunAsync(SaveButton, "Saving…", _errors, Sub() changed = AuthService.UpdateProfile(input))
        CurrentPasswordInput.Clear()
        If Not ok Then Return

        AnswerBox.Clear()
        If changed Then
            ' Keep "Remember me" pointing at the renamed account.
            If oldUsername IsNot Nothing AndAlso String.Equals(RememberedLogin.Load(), oldUsername, StringComparison.OrdinalIgnoreCase) Then
                RememberedLogin.Save(input.Username)
            End If
            MainWindow.Instance?.ShowToast("Profile updated.")
        Else
            MainWindow.Instance?.ShowToast("No changes to save.", ToastKind.Info)
        End If
        SafeReload()
    End Sub

    Private Sub Discard_Click(sender As Object, e As RoutedEventArgs)
        SafeReload()
    End Sub

    Private Sub ChangePassword_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance?.NavigateTo(PageKey.ChangePassword)
    End Sub

    Private Sub AddAdmin_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance?.NavigateTo(PageKey.AddAdmin)
    End Sub
End Class
