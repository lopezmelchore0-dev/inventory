Imports System.Windows.Threading

''' <summary>
''' Password recovery without email: Username → Security Question + Answer → New Password.
''' Unknown usernames get a stand-in question and the same generic failure, so nothing reveals whether an account exists.
''' </summary>
Class ForgotPasswordView

    Public Const SuccessMessage As String = "Password reset successfully. You can now log in with your new password."

    Private ReadOnly _errors As FormErrors
    Private _step As Integer = 1
    Private _username As String
    Private _ticket As String
    Private _redirectTimer As DispatcherTimer

    Public Sub New()
        InitializeComponent()
        _errors = New FormErrors(GeneralError)
        _errors.Register("username", UsernameError, UsernameBox)
        _errors.Register("answer", AnswerError, AnswerBox)
        _errors.Register("password", NewPasswordError, NewPasswordInput)
        _errors.Register("confirm", ConfirmError, ConfirmInput)
        AuthUi.SubmitOnEnter(FormCard, AddressOf Submit)
        AuthUi.CancelOnEscape(FormCard, Sub() BackToLogin_Click(Nothing, Nothing), ActionButton)
        AuthUi.MakeResponsive(Me, FormCard)
        AddHandler Loaded, Sub(s, e) UsernameBox.Focus()
        AddHandler Unloaded, Sub(s, e) _redirectTimer?.Stop()
    End Sub

    Private Sub Action_Click(sender As Object, e As RoutedEventArgs)
        Submit()
    End Sub

    Private Async Sub Submit()
        If Not ActionButton.IsEnabled Then Return
        Select Case _step
            Case 1
                Dim username = UsernameBox.Text.Trim()
                Dim question As String = Nothing
                If Await AuthUi.RunAsync(ActionButton, "Checking…", _errors, Sub() question = AuthService.BeginRecovery(username)) Then
                    _username = username
                    QuestionText.Text = question
                    ShowStep(2)
                    AnswerBox.Focus()
                End If

            Case 2
                Dim answer = AnswerBox.Text
                Dim username = _username
                Dim ticket As String = Nothing
                If Await AuthUi.RunAsync(ActionButton, "Verifying…", _errors, Sub() ticket = AuthService.VerifyRecoveryAnswer(username, answer)) Then
                    _ticket = ticket
                    AnswerBox.Clear()
                    NewPasswordInput.StrengthUsername = _username
                    ShowStep(3)
                    NewPasswordInput.Focus()
                Else
                    AnswerBox.SelectAll()
                    AnswerBox.Focus()
                End If

            Case 3
                Dim newPassword = NewPasswordInput.Password
                Dim confirm = ConfirmInput.Password
                Dim ticket = _ticket
                If Await AuthUi.RunAsync(ActionButton, "Resetting password…", _errors, Sub() AuthService.ResetPassword(ticket, newPassword, confirm)) Then
                    _ticket = Nothing
                    NewPasswordInput.Clear()
                    ConfirmInput.Clear()
                    ShowStep(4)
                    _redirectTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(3)}
                    AddHandler _redirectTimer.Tick, Sub(s, e) GoToLogin()
                    _redirectTimer.Start()
                ElseIf GeneralError.Text.Contains("expired") Then
                    ' The reset window ran out: start again from the username.
                    ShowStep(1)
                    UsernameBox.Focus()
                End If

            Case Else
                GoToLogin()
        End Select
    End Sub

    Private Sub ShowStep(stepNumber As Integer)
        _step = stepNumber
        Step1Panel.Visibility = If(stepNumber = 1, Visibility.Visible, Visibility.Collapsed)
        Step2Panel.Visibility = If(stepNumber = 2, Visibility.Visible, Visibility.Collapsed)
        Step3Panel.Visibility = If(stepNumber = 3, Visibility.Visible, Visibility.Collapsed)
        DonePanel.Visibility = If(stepNumber = 4, Visibility.Visible, Visibility.Collapsed)

        Dim done = StatusPalette.Find("AccentButtonBrush")
        Dim todo = StatusPalette.Find("LavenderBrush")
        StepBar1.Background = done
        StepBar2.Background = If(stepNumber >= 2, done, todo)
        StepBar3.Background = If(stepNumber >= 3, done, todo)

        Select Case stepNumber
            Case 1
                StepText.Text = "Step 1 of 3 · Username"
                ActionButton.Content = "Continue"
            Case 2
                StepText.Text = "Step 2 of 3 · Security question"
                ActionButton.Content = "Verify Answer"
            Case 3
                StepText.Text = "Step 3 of 3 · New password"
                ActionButton.Content = "Reset Password"
            Case Else
                StepText.Text = "Done"
                ActionButton.Content = "Go to Login"
        End Select
    End Sub

    Private Sub GoToLogin()
        _redirectTimer?.Stop()
        Dim username = If(_step = 4, _username, Nothing)
        MainWindow.Instance?.ShowAuth(AuthScreen.Login, If(_step = 4, SuccessMessage, Nothing), username)
    End Sub

    Private Sub BackToLogin_Click(sender As Object, e As RoutedEventArgs)
        _ticket = Nothing
        GoToLogin()
    End Sub
End Class
