Imports MySqlConnector

''' <summary>Invalid user input, with messages keyed by form field.</summary>
Public Class InputValidationException
    Inherits Exception

    Public ReadOnly Property Errors As Dictionary(Of String, String)

    Public Sub New(errors As Dictionary(Of String, String))
        MyBase.New(If(errors.Count > 0, errors.Values.First(), "The entered information is not valid."))
        Me.Errors = errors
    End Sub

    Public Sub New(field As String, message As String)
        Me.New(New Dictionary(Of String, String) From {{field, message}})
    End Sub
End Class

''' <summary>An operation that is not allowed in the current state (e.g. deleting a supplier still in use).</summary>
Public Class BusinessRuleException
    Inherits Exception

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub
End Class

''' <summary>A protected operation was attempted without a signed-in Admin.</summary>
Public Class NotAuthenticatedException
    Inherits Exception

    Public Sub New()
        MyBase.New("Your session has ended. Please log in again.")
    End Sub
End Class

''' <summary>Turns exceptions into messages a shop owner can act on.</summary>
Public NotInheritable Class ErrorText
    Private Sub New()
    End Sub

    Public Shared Function IsConnectionError(ex As Exception) As Boolean
        Dim current = ex
        While current IsNot Nothing
            Dim mysqlEx = TryCast(current, MySqlException)
            ' Missing database/tables are treated like connection problems: Retry re-creates the schema.
            If mysqlEx IsNot Nothing AndAlso
               (mysqlEx.ErrorCode = MySqlErrorCode.UnableToConnectToHost OrElse mysqlEx.ErrorCode = MySqlErrorCode.AccessDenied OrElse
                mysqlEx.ErrorCode = MySqlErrorCode.UnknownDatabase OrElse mysqlEx.ErrorCode = MySqlErrorCode.NoSuchTable) Then
                Return True
            End If
            If TypeOf current Is Net.Sockets.SocketException OrElse TypeOf current Is TimeoutException Then Return True
            current = current.InnerException
        End While
        Return False
    End Function

    Public Shared Function Describe(ex As Exception) As String
        If TypeOf ex Is InputValidationException OrElse TypeOf ex Is BusinessRuleException OrElse
           TypeOf ex Is NotAuthenticatedException Then Return ex.Message
        Dim mysqlEx = TryCast(ex, MySqlException)
        If mysqlEx IsNot Nothing Then
            Select Case mysqlEx.ErrorCode
                Case MySqlErrorCode.UnableToConnectToHost
                    Return "MySQL is not reachable. Make sure MySQL is started in the XAMPP Control Panel."
                Case MySqlErrorCode.AccessDenied
                    Return "MySQL rejected the user name or password in dbconfig.json."
                Case MySqlErrorCode.UnknownDatabase, MySqlErrorCode.NoSuchTable
                    Return "The inventory database or one of its tables is missing. Click Retry to create it again."
                Case MySqlErrorCode.DuplicateKeyEntry
                    Return "A record with the same name already exists."
                Case Else
                    Return $"Database error: {mysqlEx.Message}"
            End Select
        End If
        If IsConnectionError(ex) Then Return "MySQL is not reachable. Make sure MySQL is started in the XAMPP Control Panel."
        Return ex.Message
    End Function

    ''' <summary>The underlying driver/socket messages (outermost first), for diagnosing connection problems.</summary>
    Public Shared Function TechnicalDetails(ex As Exception) As String
        Dim parts As New List(Of String)
        Dim current = ex
        While current IsNot Nothing
            Dim text = current.Message?.Trim()
            Dim mysqlEx = TryCast(current, MySqlException)
            If mysqlEx IsNot Nothing Then text = $"{text} (MySQL error {CInt(mysqlEx.ErrorCode)})"
            If Not String.IsNullOrEmpty(text) AndAlso Not parts.Contains(text) Then parts.Add(text)
            current = current.InnerException
        End While
        Return String.Join(" → ", parts)
    End Function
End Class
