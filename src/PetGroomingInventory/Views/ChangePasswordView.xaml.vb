Class ChangePasswordView

    Public Const SuccessMessage As String = "Password changed successfully."

    Private ReadOnly _errors As FormErrors

    Public Sub New()
        InitializeComponent()
        _errors = New FormErrors(GeneralError)
        _errors.Register("current", CurrentError, CurrentInput)
        _errors.Register("password", NewError, NewInput)
        _errors.Register("confirm", ConfirmError, ConfirmInput)
        AuthUi.SubmitOnEnter(Form, AddressOf Submit)
        AddHandler IsVisibleChanged,
            Sub(s, e)
                If IsVisible Then
                    NewInput.StrengthUsername = AuthSession.Username
                    CurrentInput.Focus()
                Else
                    ClearFields()
                    SuccessBorder.Visibility = Visibility.Collapsed
                End If
            End Sub
    End Sub

    Private Sub ClearFields()
        CurrentInput.Clear()
        NewInput.Clear()
        ConfirmInput.Clear()
    End Sub

    Private Sub Change_Click(sender As Object, e As RoutedEventArgs)
        Submit()
    End Sub

    Private Async Sub Submit()
        If Not ChangeButton.IsEnabled Then Return
        SuccessBorder.Visibility = Visibility.Collapsed
        Dim current = CurrentInput.Password
        Dim newPassword = NewInput.Password
        Dim confirm = ConfirmInput.Password
        Dim ok = Await AuthUi.RunAsync(ChangeButton, "Changing password…", _errors,
                                       Sub() AuthService.ChangePassword(current, newPassword, confirm))
        If Not ok Then
            If CurrentError.Visibility = Visibility.Visible Then CurrentInput.Clear()
            Return
        End If
        ClearFields()
        SuccessBorder.Visibility = Visibility.Visible
        MainWindow.Instance?.ShowToast(SuccessMessage)
    End Sub

    Private Sub BackToProfile_Click(sender As Object, e As RoutedEventArgs)
        MainWindow.Instance?.NavigateTo(PageKey.Profile)
    End Sub
End Class
