Imports MySqlConnector

Public NotInheritable Class SupplierRepository
    Private Sub New()
    End Sub

    Private Const SelectSql As String =
        "SELECT s.id, s.name, s.contact_person, s.phone, s.address, s.email, s.is_sample, " &
        "(SELECT COUNT(*) FROM items i WHERE i.supplier_id = s.id) AS item_count " &
        "FROM suppliers s "

    Public Shared Function GetAll(conn As MySqlConnection) As List(Of Supplier)
        Return Db.Query(Of Supplier)(conn, Nothing, SelectSql & "ORDER BY s.name", AddressOf Map)
    End Function

    Public Shared Function GetById(conn As MySqlConnection, tx As MySqlTransaction, id As Integer) As Supplier
        Return Db.Query(Of Supplier)(conn, tx, SelectSql & "WHERE s.id = @id", AddressOf Map, Db.P("@id", id)).FirstOrDefault()
    End Function

    Public Shared Function FindIdByName(conn As MySqlConnection, tx As MySqlTransaction, name As String) As Integer?
        Dim value = Db.Scalar(conn, tx, "SELECT id FROM suppliers WHERE name = @name", Db.P("@name", name.Trim()))
        If value Is Nothing Then Return Nothing
        Return Convert.ToInt32(value)
    End Function

    Public Shared Function NameExists(conn As MySqlConnection, tx As MySqlTransaction, name As String, excludeId As Integer) As Boolean
        Return Db.Count(conn, tx, "SELECT COUNT(*) FROM suppliers WHERE name = @name AND id <> @id",
                        Db.P("@name", name.Trim()), Db.P("@id", excludeId)) > 0
    End Function

    Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, s As Supplier) As Integer
        Return Db.Insert(conn, tx,
            "INSERT INTO suppliers (name, contact_person, phone, address, email, is_sample) " &
            "VALUES (@name, @contact, @phone, @address, @email, @sample)",
            Db.P("@name", s.Name.Trim()),
            Db.P("@contact", s.ContactPerson.Trim()),
            Db.P("@phone", s.Phone.Trim()),
            Db.P("@address", s.Address.Trim()),
            Db.P("@email", Db.NullIfBlank(s.Email)),
            Db.P("@sample", s.IsSample))
    End Function

    Public Shared Sub Update(conn As MySqlConnection, tx As MySqlTransaction, s As Supplier)
        Db.Execute(conn, tx,
            "UPDATE suppliers SET name = @name, contact_person = @contact, phone = @phone, address = @address, email = @email " &
            "WHERE id = @id",
            Db.P("@name", s.Name.Trim()),
            Db.P("@contact", s.ContactPerson.Trim()),
            Db.P("@phone", s.Phone.Trim()),
            Db.P("@address", s.Address.Trim()),
            Db.P("@email", Db.NullIfBlank(s.Email)),
            Db.P("@id", s.Id))
    End Sub

    Public Shared Function CountItems(conn As MySqlConnection, tx As MySqlTransaction, id As Integer) As Integer
        Return Db.Count(conn, tx, "SELECT COUNT(*) FROM items WHERE supplier_id = @id", Db.P("@id", id))
    End Function

    Public Shared Sub Delete(conn As MySqlConnection, tx As MySqlTransaction, id As Integer)
        Db.Execute(conn, tx, "DELETE FROM suppliers WHERE id = @id", Db.P("@id", id))
    End Sub

    Private Shared Function Map(r As MySqlDataReader) As Supplier
        Return New Supplier With {
            .Id = Db.GetInt(r, "id"),
            .Name = Db.GetString(r, "name"),
            .ContactPerson = Db.GetString(r, "contact_person"),
            .Phone = Db.GetString(r, "phone"),
            .Address = Db.GetString(r, "address"),
            .Email = Db.GetString(r, "email"),
            .IsSample = Db.GetBool(r, "is_sample"),
            .ItemCount = Db.GetInt(r, "item_count")
        }
    End Function
End Class
