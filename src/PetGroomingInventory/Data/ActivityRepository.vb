Imports MySqlConnector

Public NotInheritable Class ActivityRepository
    Private Sub New()
    End Sub

    ''' <summary>performedBy defaults to the signed-in Admin (pass it for account events that happen before sign-in).</summary>
    Public Shared Sub Log(conn As MySqlConnection, tx As MySqlTransaction, subjectName As String, activityType As String, description As String,
                          Optional performedBy As String = Nothing)
        Db.Execute(conn, tx,
            "INSERT INTO activity_log (subject_name, activity_type, description, performed_by) VALUES (@subject, @type, @description, @by)",
            Db.P("@subject", Fmt.Truncate(subjectName, 150)),
            Db.P("@type", activityType),
            Db.P("@description", Fmt.Truncate(description, 500)),
            Db.P("@by", If(performedBy, AuthSession.Username)))
    End Sub

    Public Shared Function GetRecent(conn As MySqlConnection, limit As Integer) As List(Of ActivityEntry)
        Return Db.Query(Of ActivityEntry)(conn, Nothing,
            "SELECT * FROM activity_log ORDER BY created_at DESC, id DESC LIMIT @limit", AddressOf Map, Db.P("@limit", limit))
    End Function

    Public Shared Function Search(conn As MySqlConnection, fromDate As Date?, toDate As Date?, activityType As String,
                                  text As String, limit As Integer) As List(Of ActivityEntry)
        Dim conditions As New List(Of String)
        Dim params As New List(Of MySqlParameter) From {Db.P("@limit", limit)}
        If fromDate.HasValue Then
            conditions.Add("created_at >= @from")
            params.Add(Db.P("@from", fromDate.Value.Date))
        End If
        If toDate.HasValue Then
            conditions.Add("created_at < @to")
            params.Add(Db.P("@to", toDate.Value.Date.AddDays(1)))
        End If
        If Not String.IsNullOrWhiteSpace(activityType) Then
            conditions.Add("activity_type = @type")
            params.Add(Db.P("@type", activityType))
        End If
        If Not String.IsNullOrWhiteSpace(text) Then
            conditions.Add("(subject_name LIKE @text OR description LIKE @text)")
            params.Add(Db.P("@text", "%" & EscapeLike(text.Trim()) & "%"))
        End If
        Dim where = If(conditions.Count > 0, "WHERE " & String.Join(" AND ", conditions), "")
        Return Db.Query(Of ActivityEntry)(conn, Nothing,
            $"SELECT * FROM activity_log {where} ORDER BY created_at DESC, id DESC LIMIT @limit", AddressOf Map, params.ToArray())
    End Function

    Public Shared Function GetTypes(conn As MySqlConnection) As List(Of String)
        Return Db.Query(Of String)(conn, Nothing, "SELECT DISTINCT activity_type FROM activity_log ORDER BY activity_type",
                                   Function(r) Db.GetString(r, "activity_type"))
    End Function

    Public Shared Function GetLatestOfType(conn As MySqlConnection, activityType As String) As ActivityEntry
        Return Db.Query(Of ActivityEntry)(conn, Nothing,
            "SELECT * FROM activity_log WHERE activity_type = @type ORDER BY created_at DESC, id DESC LIMIT 1",
            AddressOf Map, Db.P("@type", activityType)).FirstOrDefault()
    End Function

    Private Shared Function EscapeLike(value As String) As String
        Return value.Replace("\", "\\").Replace("%", "\%").Replace("_", "\_")
    End Function

    Public Shared Function Map(r As MySqlDataReader) As ActivityEntry
        Return New ActivityEntry With {
            .Id = Db.GetInt(r, "id"),
            .SubjectName = Db.GetString(r, "subject_name"),
            .ActivityType = Db.GetString(r, "activity_type"),
            .Description = Db.GetString(r, "description"),
            .PerformedBy = Db.GetString(r, "performed_by"),
            .CreatedAt = Db.GetDate(r, "created_at")
        }
    End Function
End Class
