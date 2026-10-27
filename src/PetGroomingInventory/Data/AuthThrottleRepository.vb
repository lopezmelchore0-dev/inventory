Imports MySqlConnector

''' <summary>
''' Counts failed sign-in / recovery attempts per key. After MaxFailures within WindowMinutes the key is
''' locked for LockMinutes. Times come from the database server so they are consistent.
''' </summary>
Public NotInheritable Class AuthThrottleRepository
    Private Sub New()
    End Sub

    Public Const MaxFailures As Integer = 5
    Public Const WindowMinutes As Integer = 15
    Public Const LockMinutes As Integer = 15

    Public Shared Function LoginKey(username As String) As String
        Return "login:" & NormalizeKeyPart(username)
    End Function

    Public Shared Function RecoveryKey(username As String) As String
        Return "recovery:" & NormalizeKeyPart(username)
    End Function

    Private Shared Function NormalizeKeyPart(username As String) As String
        Dim text = If(username, "").Trim().ToLowerInvariant()
        Return If(text.Length > 64, text.Substring(0, 64), text)
    End Function

    ''' <summary>Whole minutes left on the lock (at least 1), or Nothing when not locked.</summary>
    Public Shared Function LockedMinutesLeft(conn As MySqlConnection, tx As MySqlTransaction, key As String) As Integer?
        Dim seconds = Db.Scalar(conn, tx,
            "SELECT TIMESTAMPDIFF(SECOND, NOW(), locked_until) FROM auth_throttle WHERE throttle_key = @key AND locked_until > NOW()",
            Db.P("@key", key))
        If seconds Is Nothing Then Return Nothing
        Return Math.Max(1, CInt(Math.Ceiling(Convert.ToDouble(seconds) / 60.0)))
    End Function

    ''' <summary>Records a failure. Returns True when this failure locked the key.</summary>
    Public Shared Function RegisterFailure(conn As MySqlConnection, tx As MySqlTransaction, key As String) As Boolean
        ' A stale window or an expired lock starts counting again from 1.
        Db.Execute(conn, tx,
            "INSERT INTO auth_throttle (throttle_key, failed_count, first_failed_at, locked_until) VALUES (@key, 1, NOW(), NULL) " &
            "ON DUPLICATE KEY UPDATE " &
            "failed_count = IF(first_failed_at < NOW() - INTERVAL @window MINUTE OR (locked_until IS NOT NULL AND locked_until <= NOW()), 1, failed_count + 1), " &
            "first_failed_at = IF(first_failed_at < NOW() - INTERVAL @window MINUTE OR (locked_until IS NOT NULL AND locked_until <= NOW()), NOW(), first_failed_at), " &
            "locked_until = IF(locked_until IS NOT NULL AND locked_until <= NOW(), NULL, locked_until)",
            Db.P("@key", key), Db.P("@window", WindowMinutes))
        Return Db.Execute(conn, tx,
            "UPDATE auth_throttle SET locked_until = NOW() + INTERVAL @lock MINUTE " &
            "WHERE throttle_key = @key AND failed_count >= @max AND locked_until IS NULL",
            Db.P("@key", key), Db.P("@lock", LockMinutes), Db.P("@max", MaxFailures)) > 0
    End Function

    Public Shared Sub Clear(conn As MySqlConnection, tx As MySqlTransaction, key As String)
        Db.Execute(conn, tx, "DELETE FROM auth_throttle WHERE throttle_key = @key", Db.P("@key", key))
    End Sub
End Class
