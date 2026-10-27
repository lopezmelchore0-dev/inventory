Imports System.Threading.Tasks
Imports System.Windows.Controls.Primitives

Public Enum AuthScreen
    Login
    Register
    ForgotPassword
End Enum

''' <summary>
''' Shared behaviour for the account forms: run the (slow, hashing) service call in the background with a
''' loading state on the button, then show field errors / a general message the same way everywhere.
''' </summary>
Public NotInheritable Class AuthUi
    Private Sub New()
    End Sub

    ''' <summary>Runs work off the UI thread. Returns True when it succeeded; errors are shown on the form.</summary>
    Public Shared Async Function RunAsync(button As Button, busyText As String, errors As FormErrors, work As Action) As Task(Of Boolean)
        Dim original = button.Content
        Dim root = Window.GetWindow(button)
        button.IsEnabled = False
        button.Content = busyText
        If root IsNot Nothing Then root.Cursor = Cursors.Wait
        errors.Clear()
        Try
            Await Task.Run(work)
            Return True
        Catch ex As InputValidationException
            errors.Show(ex.Errors)
        Catch ex As BusinessRuleException
            errors.ShowGeneral(ex.Message)
        Catch ex As NotAuthenticatedException
            MainWindow.Instance?.ShowAuth(AuthScreen.Login, ex.Message)
        Catch ex As Exception
            If ErrorText.IsConnectionError(ex) Then
                MainWindow.Instance?.ShowDbError(ex)
            Else
                errors.ShowGeneral(ErrorText.Describe(ex))
            End If
        Finally
            button.Content = original
            button.IsEnabled = True
            If root IsNot Nothing Then root.ClearValue(FrameworkElement.CursorProperty)
        End Try
        Return False
    End Function

    ''' <summary>Enter anywhere in the form submits it (Shift+Enter and combo boxes with an open list are left alone).</summary>
    Public Shared Sub SubmitOnEnter(form As UIElement, submit As Action)
        AddHandler form.PreviewKeyDown,
            Sub(s, e)
                If e.Key <> Key.Enter OrElse Keyboard.Modifiers <> ModifierKeys.None Then Return
                Dim combo = TryCast(e.OriginalSource, ComboBox)
                If combo IsNot Nothing AndAlso combo.IsDropDownOpen Then Return
                If TypeOf e.OriginalSource Is ButtonBase Then Return
                e.Handled = True
                submit()
            End Sub
    End Sub
End Class
