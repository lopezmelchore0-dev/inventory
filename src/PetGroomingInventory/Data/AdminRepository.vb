Imports MySqlConnector

''' <summary>An account row including its hashes. Only the auth service sees this; the UI gets AdminAccount.</summary>
Friend Class AdminCredentials
    Public Property Account As AdminAccount
    Public Property PasswordHash As String
    Public Property AnswerHash As String
End Class

Public NotInheritable Class AdminRepository
    Private Sub New()
    End Sub

    Public Const DecoyKeySetting As String = "auth_decoy_key"

    Private Const SelectSql As String =
        "SELECT id, full_name, username, password_hash, security_question, security_answer_hash, role, " &
        "last_login_at, password_changed_at, created_at FROM admin_accounts "

    Public Shared Function CountAll(conn As MySqlConnection, tx As MySqlTransaction) As Integer
        Return Db.Count(conn, tx, "SELECT COUNT(*) FROM admin_accounts")
    End Function

    Friend Shared Function GetByUsername(conn As MySqlConnection, tx As MySqlTransaction, username As String) As AdminCredentials
        Return Db.Query(Of AdminCredentials)(conn, tx, SelectSql & "WHERE username = @username",
                                             AddressOf Map, Db.P("@username", If(username, "").Trim())).FirstOrDefault()
    End Function

    Friend Shared Function GetById(conn As MySqlConnection, tx As MySqlTransaction, id As Integer) As AdminCredentials
        Return Db.Query(Of AdminCredentials)(conn, tx, SelectSql & "WHERE id = @id", AddressOf Map, Db.P("@id", id)).FirstOrDefault()
    End Function

    ''' <summary>Case-insensitive (the table uses a _ci collation).</summary>
    Public Shared Function UsernameExists(conn As MySqlConnection, tx As MySqlTransaction, username As String, excludeId As Integer) As Boolean
        Return Db.Count(conn, tx, "SELECT COUNT(*) FROM admin_accounts WHERE username = @username AND id <> @id",
                        Db.P("@username", username.Trim()), Db.P("@id", excludeId)) > 0
    End Function

    ''' <summary>The role column is left to its DEFAULT ('Admin'); it is never written from input.</summary>
    Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, fullName As String, username As String,
                                  passwordHash As String, question As String, answerHash As String) As Integer
        Return Db.Insert(conn, tx,
            "INSERT INTO admin_accounts (full_name, username, password_hash, security_question, security_answer_hash) " &
            "VALUES (@name, @username, @password, @question, @answer)",
            Db.P("@name", fullName.Trim()), Db.P("@username", username.Trim()), Db.P("@password", passwordHash),
            Db.P("@question", question), Db.P("@answer", answerHash))
    End Function

    ''' <summary>answerHash Nothing keeps the current answer.</summary>
    Public Shared Sub UpdateProfile(conn As MySqlConnection, tx As MySqlTransaction, id As Integer, fullName As String, username As String,
                                    question As String, answerHash As String)
        Db.Execute(conn, tx,
            "UPDATE admin_accounts SET full_name = @name, username = @username, security_question = @question, " &
            "security_answer_hash = COALESCE(@answer, security_answer_hash) WHERE id = @id",
            Db.P("@name", fullName.Trim()), Db.P("@username", username.Trim()), Db.P("@question", question),
            Db.P("@answer", answerHash), Db.P("@id", id))
    End Sub

    Public Shared Sub UpdatePassword(conn As MySqlConnection, tx As MySqlTransaction, id As Integer, passwordHash As String)
        Db.Execute(conn, tx,
            "UPDATE admin_accounts SET password_hash = @password, password_changed_at = NOW() WHERE id = @id",
            Db.P("@password", passwordHash), Db.P("@id", id))
    End Sub

    Public Shared Sub TouchLastLogin(conn As MySqlConnection, tx As MySqlTransaction, id As Integer)
        Db.Execute(conn, tx, "UPDATE admin_accounts SET last_login_at = NOW() WHERE id = @id", Db.P("@id", id))
    End Sub

    ''' <summary>The per-install secret for stand-in recovery questions (created by schema.sql; re-created if missing).</summary>
    Public Shared Function GetDecoyKey(conn As MySqlConnection, tx As MySqlTransaction) As String
        Dim value = TryCast(Db.Scalar(conn, tx, "SELECT setting_value FROM settings WHERE setting_key = @key", Db.P("@key", DecoyKeySetting)), String)
        If Not String.IsNullOrEmpty(value) Then Return value
        Db.Execute(conn, tx, "INSERT IGNORE INTO settings (setting_key, setting_value) VALUES (@key, SHA2(UUID(), 256))", Db.P("@key", DecoyKeySetting))
        Return Convert.ToString(Db.Scalar(conn, tx, "SELECT setting_value FROM settings WHERE setting_key = @key", Db.P("@key", DecoyKeySetting)))
    End Function

    Private Shared Function Map(r As MySqlDataReader) As AdminCredentials
        Dim lastLogin = r.GetOrdinal("last_login_at")
        Return New AdminCredentials With {
            .Account = New AdminAccount With {
                .Id = Db.GetInt(r, "id"),
                .FullName = Db.GetString(r, "full_name"),
                .Username = Db.GetString(r, "username"),
                .SecurityQuestion = Db.GetString(r, "security_question"),
                .Role = Db.GetString(r, "role"),
                .LastLoginAt = If(r.IsDBNull(lastLogin), CType(Nothing, Date?), Convert.ToDateTime(r.GetValue(lastLogin))),
                .PasswordChangedAt = Db.GetDate(r, "password_changed_at"),
                .CreatedAt = Db.GetDate(r, "created_at")
            },
            .PasswordHash = Db.GetString(r, "password_hash"),
            .AnswerHash = Db.GetString(r, "security_answer_hash")
        }
    End Function
End Class
