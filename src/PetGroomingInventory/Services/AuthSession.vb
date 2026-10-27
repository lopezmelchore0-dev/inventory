''' <summary>
''' The signed-in Admin for this run of the app. Held in memory only: closing the app or logging out ends it.
''' </summary>
Public NotInheritable Class AuthSession
    Private Sub New()
    End Sub

    Private Shared _current As AdminAccount

    Public Shared ReadOnly Property Current As AdminAccount
        Get
            Return _current
        End Get
    End Property

    Public Shared ReadOnly Property IsAuthenticated As Boolean
        Get
            Return _current IsNot Nothing
        End Get
    End Property

    ''' <summary>Username of the signed-in Admin, or Nothing.</summary>
    Public Shared ReadOnly Property Username As String
        Get
            Return _current?.Username
        End Get
    End Property

    Public Shared Function Require() As AdminAccount
        If _current Is Nothing Then Throw New NotAuthenticatedException()
        Return _current
    End Function

    Friend Shared Sub SignIn(account As AdminAccount)
        If account Is Nothing Then Throw New ArgumentNullException(NameOf(account))
        _current = account
    End Sub

    Friend Shared Sub Refresh(account As AdminAccount)
        If _current Is Nothing OrElse account Is Nothing OrElse account.Id <> _current.Id Then Return
        _current = account
    End Sub

    Public Shared Sub SignOut()
        _current = Nothing
    End Sub
End Class
