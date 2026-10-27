''' <summary>Modal dialogs with a dimmed main window behind them.</summary>
Public NotInheritable Class Dialogs
    Private Sub New()
    End Sub

    Public Shared Function ShowModal(window As Window) As Boolean
        Dim main = MainWindow.Instance
        Dim owner = Application.Current?.Windows.OfType(Of Window)().FirstOrDefault(Function(w) w.IsActive AndAlso w IsNot window)
        If owner Is Nothing AndAlso main IsNot Nothing AndAlso main.IsLoaded Then owner = main
        If owner IsNot Nothing AndAlso owner.IsLoaded Then
            window.Owner = owner
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner
        Else
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen
        End If

        main?.SetOverlay(True)
        Try
            Return window.ShowDialog().GetValueOrDefault()
        Finally
            main?.SetOverlay(False)
        End Try
    End Function

    Public Shared Function Confirm(title As String, message As String, confirmText As String, Optional destructive As Boolean = False) As Boolean
        Dim kind = If(destructive, MessageKind.Danger, MessageKind.Warning)
        Return ShowModal(New MessageDialog(title, message, kind, confirmText, "Cancel"))
    End Function

    Public Shared Sub ShowInfo(title As String, message As String, Optional kind As MessageKind = MessageKind.Info)
        ShowModal(New MessageDialog(title, message, kind, "OK", Nothing))
    End Sub

    Public Shared Sub ShowError(title As String, message As String)
        ShowModal(New MessageDialog(title, message, MessageKind.Error, "OK", Nothing))
    End Sub
End Class
