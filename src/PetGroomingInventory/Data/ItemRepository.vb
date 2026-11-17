Imports MySqlConnector

Public NotInheritable Class ItemRepository
    Private Sub New()
    End Sub

    Private Const SelectSql As String =
        "SELECT i.id, i.name, i.category_id, c.name AS category_name, i.pet_type, i.quantity, i.unit, " &
        "i.retail_price, i.supplier_id, s.name AS supplier_name, i.expiration_date, i.description, i.is_sample, i.created_at " &
        "FROM items i " &
        "JOIN categories c ON c.id = i.category_id " &
        "JOIN suppliers s ON s.id = i.supplier_id "

    Public Shared Function GetAll(conn As MySqlConnection) As List(Of Item)
        Return Db.Query(Of Item)(conn, Nothing, SelectSql & "ORDER BY i.name", AddressOf Map)
    End Function

    Public Shared Function GetById(conn As MySqlConnection, tx As MySqlTransaction, id As Integer) As Item
        Return Db.Query(Of Item)(conn, tx, SelectSql & "WHERE i.id = @id", AddressOf Map, Db.P("@id", id)).FirstOrDefault()
    End Function

    ''' <summary>Short descriptions already used by items (long notes are skipped), offered as suggestions on the next item.</summary>
    Public Shared Function GetDistinctDescriptions(conn As MySqlConnection, maxLength As Integer) As List(Of String)
        Return Db.Query(Of String)(conn, Nothing,
            "SELECT DISTINCT TRIM(description) AS description FROM items " &
            "WHERE description IS NOT NULL AND TRIM(description) <> '' AND CHAR_LENGTH(TRIM(description)) <= @max " &
            "ORDER BY description",
            Function(r) Db.GetString(r, "description"), Db.P("@max", maxLength))
    End Function

    ''' <summary>Units already used by items, offered as suggestions so a custom unit is typed only once.</summary>
    Public Shared Function GetDistinctUnits(conn As MySqlConnection) As List(Of String)
        Return Db.Query(Of String)(conn, Nothing,
            "SELECT DISTINCT TRIM(unit) AS unit FROM items WHERE unit IS NOT NULL AND TRIM(unit) <> '' ORDER BY unit",
            Function(r) Db.GetString(r, "unit"))
    End Function

    ''' <summary>Pet types already used by items (built-in and custom), so a custom pet type is typed only once.</summary>
    Public Shared Function GetDistinctPetTypes(conn As MySqlConnection, tx As MySqlTransaction) As List(Of String)
        Return Db.Query(Of String)(conn, tx,
            "SELECT DISTINCT TRIM(pet_type) AS pet_type FROM items WHERE TRIM(pet_type) <> '' ORDER BY pet_type",
            Function(r) Db.GetString(r, "pet_type"))
    End Function

    ''' <summary>Reads the quantity with a row lock so concurrent stock changes cannot interleave.</summary>
    Public Shared Function LockQuantity(conn As MySqlConnection, tx As MySqlTransaction, id As Integer) As Decimal?
        Dim value = Db.Scalar(conn, tx, "SELECT quantity FROM items WHERE id = @id FOR UPDATE", Db.P("@id", id))
        If value Is Nothing Then Return Nothing
        Return Convert.ToDecimal(value)
    End Function

    Public Shared Function NameExists(conn As MySqlConnection, tx As MySqlTransaction, name As String, excludeId As Integer) As Boolean
        Return Db.Count(conn, tx, "SELECT COUNT(*) FROM items WHERE name = @name AND id <> @id",
                        Db.P("@name", name.Trim()), Db.P("@id", excludeId)) > 0
    End Function

    Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, item As Item) As Integer
        Return Db.Insert(conn, tx,
            "INSERT INTO items (name, category_id, pet_type, quantity, unit, retail_price, supplier_id, expiration_date, description, is_sample) " &
            "VALUES (@name, @category, @petType, @quantity, @unit, @price, @supplier, @expiration, @description, @sample)",
            Db.P("@name", item.Name.Trim()),
            Db.P("@category", item.CategoryId),
            Db.P("@petType", item.PetType),
            Db.P("@quantity", item.Quantity),
            Db.P("@unit", Db.NullIfBlank(item.Unit)),
            Db.P("@price", item.RetailPrice),
            Db.P("@supplier", item.SupplierId),
            Db.P("@expiration", item.ExpirationDate),
            Db.P("@description", Db.NullIfBlank(item.Description)),
            Db.P("@sample", item.IsSample))
    End Function

    ''' <summary>Updates descriptive fields only. Quantity changes go through stock-in / adjustment.</summary>
    Public Shared Sub UpdateDetails(conn As MySqlConnection, tx As MySqlTransaction, item As Item)
        Db.Execute(conn, tx,
            "UPDATE items SET name = @name, category_id = @category, pet_type = @petType, unit = @unit, retail_price = @price, " &
            "supplier_id = @supplier, expiration_date = @expiration, description = @description WHERE id = @id",
            Db.P("@name", item.Name.Trim()),
            Db.P("@category", item.CategoryId),
            Db.P("@petType", item.PetType),
            Db.P("@unit", Db.NullIfBlank(item.Unit)),
            Db.P("@price", item.RetailPrice),
            Db.P("@supplier", item.SupplierId),
            Db.P("@expiration", item.ExpirationDate),
            Db.P("@description", Db.NullIfBlank(item.Description)),
            Db.P("@id", item.Id))
    End Sub

    Public Shared Sub UpdateQuantity(conn As MySqlConnection, tx As MySqlTransaction, id As Integer, quantity As Decimal)
        Db.Execute(conn, tx, "UPDATE items SET quantity = @quantity WHERE id = @id", Db.P("@quantity", quantity), Db.P("@id", id))
    End Sub

    Public Shared Sub Delete(conn As MySqlConnection, tx As MySqlTransaction, id As Integer)
        Db.Execute(conn, tx, "DELETE FROM items WHERE id = @id", Db.P("@id", id))
    End Sub

    Private Shared Function Map(r As MySqlDataReader) As Item
        Return New Item With {
            .Id = Db.GetInt(r, "id"),
            .Name = Db.GetString(r, "name"),
            .CategoryId = Db.GetInt(r, "category_id"),
            .CategoryName = Db.GetString(r, "category_name"),
            .PetType = Db.GetString(r, "pet_type"),
            .Quantity = Db.GetDecimal(r, "quantity"),
            .Unit = Db.GetString(r, "unit"),
            .RetailPrice = Db.GetDecimal(r, "retail_price"),
            .SupplierId = Db.GetInt(r, "supplier_id"),
            .SupplierName = Db.GetString(r, "supplier_name"),
            .ExpirationDate = Db.GetNullableDate(r, "expiration_date"),
            .Description = Db.GetString(r, "description"),
            .IsSample = Db.GetBool(r, "is_sample"),
            .CreatedAt = Db.GetDate(r, "created_at")
        }
    End Function
End Class
