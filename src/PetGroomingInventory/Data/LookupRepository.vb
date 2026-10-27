Imports MySqlConnector

''' <summary>Categories and units share the same shape (id, name), so one repository serves both.</summary>
Public NotInheritable Class LookupRepository
    Private Sub New()
    End Sub

    ' Table and column names come from this whitelist only, never from user input.
    Private Shared Function TableName(kind As LookupKind) As String
        Return If(kind = LookupKind.Category, "categories", "units")
    End Function

    Private Shared Function ItemColumn(kind As LookupKind) As String
        Return If(kind = LookupKind.Category, "category_id", "unit_id")
    End Function

    Public Shared Function GetAll(conn As MySqlConnection, kind As LookupKind) As List(Of LookupItem)
        Dim sql = $"SELECT t.id, t.name, (SELECT COUNT(*) FROM items i WHERE i.{ItemColumn(kind)} = t.id) AS usage_count " &
                  $"FROM {TableName(kind)} t ORDER BY t.name"
        Return Db.Query(Of LookupItem)(conn, Nothing, sql,
            Function(r) New LookupItem With {
                .Id = Db.GetInt(r, "id"),
                .Name = Db.GetString(r, "name"),
                .UsageCount = Db.GetInt(r, "usage_count")
            })
    End Function

    Public Shared Function GetName(conn As MySqlConnection, tx As MySqlTransaction, kind As LookupKind, id As Integer) As String
        Return CStr(Db.Scalar(conn, tx, $"SELECT name FROM {TableName(kind)} WHERE id = @id", Db.P("@id", id)))
    End Function

    Public Shared Function FindIdByName(conn As MySqlConnection, tx As MySqlTransaction, kind As LookupKind, name As String) As Integer?
        Dim value = Db.Scalar(conn, tx, $"SELECT id FROM {TableName(kind)} WHERE name = @name", Db.P("@name", name.Trim()))
        If value Is Nothing Then Return Nothing
        Return Convert.ToInt32(value)
    End Function

    Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, kind As LookupKind, name As String) As Integer
        Return Db.Insert(conn, tx, $"INSERT INTO {TableName(kind)} (name) VALUES (@name)", Db.P("@name", name.Trim()))
    End Function

    Public Shared Function CountUsage(conn As MySqlConnection, tx As MySqlTransaction, kind As LookupKind, id As Integer) As Integer
        Return Db.Count(conn, tx, $"SELECT COUNT(*) FROM items WHERE {ItemColumn(kind)} = @id", Db.P("@id", id))
    End Function

    Public Shared Sub Delete(conn As MySqlConnection, tx As MySqlTransaction, kind As LookupKind, id As Integer)
        Db.Execute(conn, tx, $"DELETE FROM {TableName(kind)} WHERE id = @id", Db.P("@id", id))
    End Sub
End Class
