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

    ''' <summary>
    ''' Enter anywhere in the form submits it, including on a check box such as "Remember me" (Shift+Enter,
    ''' combo boxes with an open list and other buttons, which handle Enter themselves, are left alone).
    ''' </summary>
    Public Shared Sub SubmitOnEnter(form As UIElement, submit As Action)
        AddHandler form.PreviewKeyDown,
            Sub(s, e)
                If e.Key <> Key.Enter OrElse Keyboard.Modifiers <> ModifierKeys.None Then Return
                Dim combo = TryCast(e.OriginalSource, ComboBox)
                If combo IsNot Nothing AndAlso combo.IsDropDownOpen Then Return
                If TypeOf e.OriginalSource Is ButtonBase AndAlso TypeOf e.OriginalSource IsNot CheckBox Then Return
                e.Handled = True
                submit()
            End Sub
    End Sub

    ''' <summary>
    ''' Esc anywhere in the form runs <paramref name="cancel"/> (e.g. back to Login). Ignored while
    ''' <paramref name="actionButton"/> is busy (disabled) and while a combo box list is open.
    ''' </summary>
    Public Shared Sub CancelOnEscape(form As UIElement, cancel As Action, actionButton As Button)
        AddHandler form.PreviewKeyDown,
            Sub(s, e)
                If e.Key <> Key.Escape OrElse Keyboard.Modifiers <> ModifierKeys.None Then Return
                Dim combo = TryCast(e.OriginalSource, ComboBox)
                If combo IsNot Nothing AndAlso combo.IsDropDownOpen Then Return
                If Not actionButton.IsEnabled Then Return
                e.Handled = True
                cancel()
            End Sub
    End Sub

    ''' <summary>Width (DIPs) below which the auth card switches to tighter padding and margins.</summary>
    Public Const CompactWidth As Double = 520

    Private Shared ReadOnly CompactPadding As New Thickness(22, 24, 22, 24)
    Private Shared ReadOnly CompactMargin As New Thickness(12, 16, 12, 16)

    ''' <summary>
    ''' On narrow windows (high display scaling, small or portrait screens) the card's padding and margins
    ''' are reduced so the form keeps usable width. On normal windows the AuthCard style values apply unchanged.
    ''' </summary>
    Public Shared Sub MakeResponsive(host As FrameworkElement, card As Border)
        AddHandler host.SizeChanged, Sub(s, e) ApplyCompact(card, e.NewSize.Width < CompactWidth)
    End Sub

    Private Shared Sub ApplyCompact(card As Border, compact As Boolean)
        If compact Then
            card.Padding = CompactPadding
            card.Margin = CompactMargin
        Else
            card.ClearValue(Border.PaddingProperty)
            card.ClearValue(FrameworkElement.MarginProperty)
        End If
    End Sub
End Class
