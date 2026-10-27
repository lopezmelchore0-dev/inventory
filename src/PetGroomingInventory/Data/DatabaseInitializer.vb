Imports System.Text.RegularExpressions
Imports MySqlConnector

''' <summary>Creates the database and tables on startup using the embedded database/schema.sql.</summary>
Public NotInheritable Class DatabaseInitializer
    Private Sub New()
    End Sub

    Private Const SchemaResourceName As String = "PetGroomingInventory.schema.sql"

    Public Shared Sub Initialize()
        Dim databaseName = Db.Config.Database
        If String.IsNullOrWhiteSpace(databaseName) OrElse Not Regex.IsMatch(databaseName, "^[A-Za-z0-9_]{1,64}$") Then
            Throw New BusinessRuleException("The database name in dbconfig.json may only contain letters, numbers and underscores.")
        End If

        Using server As New MySqlConnection(Db.BuildConnectionString(False))
            server.Open()
            Db.Execute(server, Nothing,
                       $"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci")
        End Using

        Using conn = Db.Open()
            For Each statement In LoadSchemaStatements()
                Db.Execute(conn, Nothing, statement)
            Next
            UpgradeItemColumns(conn)
            Using tx = conn.BeginTransaction()
                MigrateLegacyCategories(conn, tx)
                tx.Commit()
            End Using
        End Using
    End Sub

    ''' <summary>Re-inserts default lookups and settings that are missing (used after a restore).</summary>
    Public Shared Sub EnsureDefaults(conn As MySqlConnection, tx As MySqlTransaction)
        For Each statement In LoadSchemaStatements().Where(Function(s) s.StartsWith("INSERT IGNORE", StringComparison.OrdinalIgnoreCase))
            Db.Execute(conn, tx, statement)
        Next
        MigrateLegacyCategories(conn, tx)
    End Sub

    ''' <summary>
    ''' Adds columns introduced after the first release to databases created before them
    ''' (CREATE TABLE IF NOT EXISTS leaves existing tables unchanged). ALTER TABLE commits implicitly, so no transaction.
    ''' </summary>
    Private Shared Sub UpgradeItemColumns(conn As MySqlConnection)
        If Not ColumnExists(conn, "items", "pet_type") Then
            Db.Execute(conn, Nothing,
                "ALTER TABLE items ADD COLUMN pet_type VARCHAR(4) NOT NULL DEFAULT 'Both' AFTER category_id, " &
                "ADD CONSTRAINT chk_items_pet_type CHECK (pet_type IN ('Dog', 'Cat', 'Both'))")
        End If
        If Not ColumnExists(conn, "items", "description") Then
            Db.Execute(conn, Nothing, "ALTER TABLE items ADD COLUMN description VARCHAR(500) NULL AFTER expiration_date")
        End If
        ' Unit was replaced by the free-text description. Make unit_id optional (never dropped, so nothing is lost) and,
        ' once, carry each item's old unit over as its description when it has none. Gated on the ALTER so a description
        ' the user clears later is not refilled on the next start-up.
        If IsColumnRequired(conn, "items", "unit_id") Then
            Db.Execute(conn, Nothing, "ALTER TABLE items MODIFY COLUMN unit_id INT NULL")
            Db.Execute(conn, Nothing,
                "UPDATE items i JOIN units u ON u.id = i.unit_id SET i.description = u.name " &
                "WHERE i.description IS NULL OR TRIM(i.description) = ''")
        End If
    End Sub

    Private Shared Function IsColumnRequired(conn As MySqlConnection, table As String, column As String) As Boolean
        Return Db.Count(conn, Nothing,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column AND IS_NULLABLE = 'NO'",
            Db.P("@table", table), Db.P("@column", column)) > 0
    End Function

    Private Shared Function ColumnExists(conn As MySqlConnection, table As String, column As String) As Boolean
        Return Db.Count(conn, Nothing,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column",
            Db.P("@table", table), Db.P("@column", column)) > 0
    End Function

    ''' <summary>
    ''' The old combined "Medicine &amp; Vitamins" category becomes "Medicine" (Vitamins is now separate).
    ''' Runs on every start-up and after a restore; does nothing once migrated.
    ''' </summary>
    Private Shared Sub MigrateLegacyCategories(conn As MySqlConnection, tx As MySqlTransaction)
        Dim legacyId = Db.Scalar(conn, tx, "SELECT id FROM categories WHERE name = @name", Db.P("@name", ItemCategories.LegacyMedicineAndVitamins))
        If legacyId Is Nothing Then Return
        Dim medicineId = Db.Scalar(conn, tx, "SELECT id FROM categories WHERE name = @name", Db.P("@name", ItemCategories.Medicine))
        If medicineId Is Nothing Then
            Db.Execute(conn, tx, "UPDATE categories SET name = @name WHERE id = @id", Db.P("@name", ItemCategories.Medicine), Db.P("@id", legacyId))
        Else
            Db.Execute(conn, tx, "UPDATE items SET category_id = @medicine WHERE category_id = @legacy", Db.P("@medicine", medicineId), Db.P("@legacy", legacyId))
            Db.Execute(conn, tx, "DELETE FROM categories WHERE id = @id", Db.P("@id", legacyId))
        End If
    End Sub

    ''' <summary>Splits schema.sql into statements, skipping comments, CREATE DATABASE and USE (the configured database is used instead).</summary>
    Public Shared Function LoadSchemaStatements() As List(Of String)
        Dim assembly = GetType(DatabaseInitializer).Assembly
        Using stream = assembly.GetManifestResourceStream(SchemaResourceName)
            If stream Is Nothing Then Throw New InvalidOperationException("The embedded schema.sql resource is missing.")
            Using reader As New IO.StreamReader(stream)
                Dim lines = reader.ReadToEnd().
                    Split({vbCrLf, vbLf}, StringSplitOptions.None).
                    Where(Function(line) Not line.TrimStart().StartsWith("--"))
                Return String.Join(vbLf, lines).
                    Split(";"c).
                    Select(Function(s) s.Trim()).
                    Where(Function(s) s.Length > 0 AndAlso
                                      Not s.StartsWith("CREATE DATABASE", StringComparison.OrdinalIgnoreCase) AndAlso
                                      Not s.StartsWith("USE ", StringComparison.OrdinalIgnoreCase)).
                    ToList()
            End Using
        End Using
    End Function
End Class
