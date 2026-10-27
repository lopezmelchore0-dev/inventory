''' <summary>
''' Base class for the main pages. Reloads when the page becomes visible after data changed,
''' or immediately when data changes while it is on screen.
''' </summary>
Public Class PageBase
    Inherits UserControl

    Private _dirty As Boolean = True

    Public Sub New()
        AddHandler AppEvents.DataChanged, AddressOf OnDataChanged
        AddHandler IsVisibleChanged, AddressOf OnIsVisibleChanged
    End Sub

    Private Sub OnDataChanged(sender As Object, e As EventArgs)
        If IsVisible Then
            SafeReload()
        Else
            _dirty = True
        End If
    End Sub

    Private Sub OnIsVisibleChanged(sender As Object, e As DependencyPropertyChangedEventArgs)
        If IsVisible AndAlso _dirty Then SafeReload()
    End Sub

    Public Sub SafeReload()
        _dirty = False
        Try
            LoadData()
        Catch ex As NotAuthenticatedException
            _dirty = True
            MainWindow.Instance?.ShowAuth(AuthScreen.Login, ex.Message)
        Catch ex As Exception
            _dirty = True
            If ErrorText.IsConnectionError(ex) Then
                MainWindow.Instance?.ShowDbError(ex)
            Else
                Dialogs.ShowError("Couldn't load data", ErrorText.Describe(ex))
            End If
        End Try
    End Sub

    ''' <summary>Override to (re)load the page's data from the database.</summary>
    Protected Overridable Sub LoadData()
    End Sub
End Class
