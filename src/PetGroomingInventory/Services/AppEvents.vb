''' <summary>
''' Raised after every successful write. Pages and the shell listen and reload,
''' so the dashboard, alerts and tables always reflect the database.
''' Always raised on the UI thread (account operations run in the background to keep the UI responsive).
''' </summary>
Public NotInheritable Class AppEvents
    Private Sub New()
    End Sub

    Public Shared Event DataChanged As EventHandler

    Public Shared Sub RaiseDataChanged()
        Dim dispatcher = Application.Current?.Dispatcher
        If dispatcher IsNot Nothing AndAlso Not dispatcher.CheckAccess() Then
            dispatcher.BeginInvoke(New Action(AddressOf RaiseNow))
        Else
            RaiseNow()
        End If
    End Sub

    Private Shared Sub RaiseNow()
        RaiseEvent DataChanged(Nothing, EventArgs.Empty)
    End Sub
End Class
