Class Application

    ''' <summary>Last line of defence: show a friendly message instead of crashing.</summary>
    Private Sub Application_DispatcherUnhandledException(sender As Object, e As System.Windows.Threading.DispatcherUnhandledExceptionEventArgs)
        e.Handled = True
        Try
            If TypeOf e.Exception Is NotAuthenticatedException AndAlso Global.PetGroomingInventory.MainWindow.Instance IsNot Nothing Then
                Global.PetGroomingInventory.MainWindow.Instance.ShowAuth(AuthScreen.Login, e.Exception.Message)
            ElseIf ErrorText.IsConnectionError(e.Exception) AndAlso MainWindow IsNot Nothing Then
                Global.PetGroomingInventory.MainWindow.Instance?.ShowDbError(e.Exception)
            Else
                Dialogs.ShowError("Something went wrong", ErrorText.Describe(e.Exception))
            End If
        Catch
            MessageBox.Show(e.Exception.Message, "PawStock", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

End Class
