Imports System.Data
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports MySqlConnector

' ---- Backup file format (JSON) ----

Public Class BackupFile
    Public Property AppId As String
    Public Property FormatVersion As Integer
    Public Property CreatedAt As Date
    Public Property Checksum As String
    Public Property Data As BackupData
End Class

Public Class BackupData
    Public Property Categories As List(Of LookupRow)
    Public Property Units As List(Of LookupRow)
    Public Property Suppliers As List(Of SupplierRow)
    Public Property Items As List(Of ItemRow)
    Public Property StockTransactions As List(Of StockTransactionRow)
    Public Property Activities As List(Of ActivityRow)
    Public Property Settings As List(Of SettingRow)
End Class

Public Class LookupRow
    Public Property Id As Integer
    Public Property Name As String
End Class

Public Class SupplierRow
    Public Property Id As Integer
    Public Property Name As String
    Public Property ContactPerson As String
    Public Property Phone As String
    Public Property Address As String
    Public Property Email As String
    Public Property IsSample As Boolean
    Public Property CreatedAt As Date
    Public Property UpdatedAt As Date
End Class

Public Class ItemRow
    Public Property Id As Integer
    Public Property Name As String
    Public Property CategoryId As Integer
    Public Property Quantity As Decimal
    ' Legacy: units were replaced by the description. Old backups carry a value; new items have none.
    Public Property UnitId As Integer?
    Public Property RetailPrice As Decimal
    Public Property SupplierId As Integer
    Public Property ExpirationDate As Date?
    Public Property IsSample As Boolean
    Public Property CreatedAt As Date
    Public Property UpdatedAt As Date

    ' Added after format version 1 shipped. Omitted when null so older backups (which lack them)
    ' still match their checksum; a missing pet type restores as Both.
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property PetType As String
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property Description As String
    ' Unit of measure. Same rule: omitted when null, and older backups restore with no unit.
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property Unit As String
End Class

Public Class StockTransactionRow
    Public Property Id As Integer
    Public Property ItemId As Integer?
    Public Property ItemName As String
    Public Property TxnType As String
    Public Property QtyChange As Decimal
    Public Property QtyBefore As Decimal
    Public Property QtyAfter As Decimal
    Public Property SupplierId As Integer?
    Public Property SupplierName As String
    Public Property TxnDate As Date
    Public Property Reason As String
    Public Property Note As String
    Public Property CreatedAt As Date
    <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
    Public Property Unit As String
End Class

Public Class ActivityRow
    Public Property Id As Integer
    Public Property SubjectName As String
    Public Property ActivityType As String
    Public Property Description As String
    Public Property PerformedBy As String
    Public Property CreatedAt As Date
End Class

Public Class SettingRow
    Public Property Key As String
    Public Property Value As String
End Class

Public Class BackupResult
    Public Property FilePath As String
    Public Property ItemCount As Integer
    Public Property SupplierCount As Integer
    Public Property TransactionCount As Integer
    Public Property ActivityCount As Integer
End Class

''' <summary>
''' Backup = every table exported to a JSON file with a SHA-256 checksum.
''' Restore = validate the file completely, save a safety backup, then replace all data in one transaction.
''' </summary>
Public NotInheritable Class BackupService
    Private Sub New()
    End Sub

    Public Const AppId As String = "PawStock.PetGroomingInventory"
    Public Const FormatVersion As Integer = 1
    Private Const MaxFileBytes As Long = 200L * 1024L * 1024L

    Private Shared ReadOnly CompactJson As New JsonSerializerOptions()
    Private Shared ReadOnly IndentedJson As New JsonSerializerOptions With {.WriteIndented = True}

    Private Shared _defaultFolder As String

    ''' <summary>Documents\PawStock Backups unless overridden (tests redirect it to a temp folder).</summary>
    Public Shared Property DefaultFolder As String
        Get
            Return If(_defaultFolder, IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PawStock Backups"))
        End Get
        Set(value As String)
            _defaultFolder = value
        End Set
    End Property

    Public Shared Function EnsureDefaultFolder() As String
        IO.Directory.CreateDirectory(DefaultFolder)
        Return DefaultFolder
    End Function

    Public Shared Function SuggestedFileName(Optional prefix As String = "PawStock-backup") As String
        Return $"{prefix}-{Date.Now:yyyyMMdd-HHmmss}.json"
    End Function

    Public Shared Function ComputeChecksum(data As BackupData) As String
        Dim bytes = JsonSerializer.SerializeToUtf8Bytes(data, CompactJson)
        Return Convert.ToHexString(SHA256.HashData(bytes))
    End Function

    ' ================= Backup =================

    Public Shared Function CreateBackup(filePath As String, Optional logActivity As Boolean = True) As BackupResult
        Dim data As BackupData
        Using conn = Db.Open()
            ' Repeatable read gives a consistent snapshot of all tables.
            Using tx = conn.BeginTransaction(IsolationLevel.RepeatableRead)
                data = ReadAll(conn, tx)
                tx.Commit()
            End Using
        End Using

        Dim file As New BackupFile With {
            .AppId = AppId,
            .FormatVersion = FormatVersion,
            .CreatedAt = Date.Now,
            .Data = data,
            .Checksum = ComputeChecksum(data)
        }

        Dim folder = IO.Path.GetDirectoryName(IO.Path.GetFullPath(filePath))
        If Not String.IsNullOrEmpty(folder) Then IO.Directory.CreateDirectory(folder)
        ' Write to a temp file first so a failure never leaves a half-written backup behind.
        Dim tempPath = filePath & ".tmp"
        IO.File.WriteAllText(tempPath, JsonSerializer.Serialize(file, IndentedJson))
        IO.File.Move(tempPath, filePath, overwrite:=True)

        If logActivity Then
            Using conn = Db.Open()
                ActivityRepository.Log(conn, Nothing, IO.Path.GetFileName(filePath), ActivityTypes.BackupCreated,
                    $"Backed up {Fmt.Plural(data.Items.Count, "item")}, {Fmt.Plural(data.Suppliers.Count, "supplier")}, " &
                    $"{Fmt.Plural(data.StockTransactions.Count, "stock record")} to {filePath}")
            End Using
            AppEvents.RaiseDataChanged()
        End If

        Return New BackupResult With {
            .FilePath = filePath,
            .ItemCount = data.Items.Count,
            .SupplierCount = data.Suppliers.Count,
            .TransactionCount = data.StockTransactions.Count,
            .ActivityCount = data.Activities.Count
        }
    End Function

    Private Shared Function ReadAll(conn As MySqlConnection, tx As MySqlTransaction) As BackupData
        Return New BackupData With {
            .Categories = Db.Query(Of LookupRow)(conn, tx, "SELECT id, name FROM categories ORDER BY id",
                Function(r) New LookupRow With {.Id = Db.GetInt(r, "id"), .Name = Db.GetString(r, "name")}),
            .Units = Db.Query(Of LookupRow)(conn, tx, "SELECT id, name FROM units ORDER BY id",
                Function(r) New LookupRow With {.Id = Db.GetInt(r, "id"), .Name = Db.GetString(r, "name")}),
            .Suppliers = Db.Query(Of SupplierRow)(conn, tx, "SELECT * FROM suppliers ORDER BY id",
                Function(r) New SupplierRow With {
                    .Id = Db.GetInt(r, "id"),
                    .Name = Db.GetString(r, "name"),
                    .ContactPerson = Db.GetString(r, "contact_person"),
                    .Phone = Db.GetString(r, "phone"),
                    .Address = Db.GetString(r, "address"),
                    .Email = Db.GetString(r, "email"),
                    .IsSample = Db.GetBool(r, "is_sample"),
                    .CreatedAt = Db.GetDate(r, "created_at"),
                    .UpdatedAt = Db.GetDate(r, "updated_at")
                }),
            .Items = Db.Query(Of ItemRow)(conn, tx, "SELECT * FROM items ORDER BY id",
                Function(r) New ItemRow With {
                    .Id = Db.GetInt(r, "id"),
                    .Name = Db.GetString(r, "name"),
                    .CategoryId = Db.GetInt(r, "category_id"),
                    .Quantity = Db.GetDecimal(r, "quantity"),
                    .UnitId = Db.GetNullableInt(r, "unit_id"),
                    .RetailPrice = Db.GetDecimal(r, "retail_price"),
                    .SupplierId = Db.GetInt(r, "supplier_id"),
                    .ExpirationDate = Db.GetNullableDate(r, "expiration_date"),
                    .IsSample = Db.GetBool(r, "is_sample"),
                    .CreatedAt = Db.GetDate(r, "created_at"),
                    .UpdatedAt = Db.GetDate(r, "updated_at"),
                    .PetType = Db.GetString(r, "pet_type"),
                    .Description = Db.GetString(r, "description"),
                    .Unit = Db.GetString(r, "unit")
                }),
            .StockTransactions = Db.Query(Of StockTransactionRow)(conn, tx, "SELECT * FROM stock_transactions ORDER BY id",
                Function(r)
                    Dim t = StockRepository.Map(r)
                    Return New StockTransactionRow With {
                        .Id = t.Id, .ItemId = t.ItemId, .ItemName = t.ItemName, .TxnType = t.TxnType,
                        .QtyChange = t.QtyChange, .QtyBefore = t.QtyBefore, .QtyAfter = t.QtyAfter,
                        .SupplierId = t.SupplierId, .SupplierName = t.SupplierName, .TxnDate = t.TxnDate.Date,
                        .Reason = t.Reason, .Note = t.Note, .CreatedAt = t.CreatedAt, .Unit = t.Unit
                    }
                End Function),
            .Activities = Db.Query(Of ActivityRow)(conn, tx, "SELECT * FROM activity_log ORDER BY id",
                Function(r)
                    Dim a = ActivityRepository.Map(r)
                    Return New ActivityRow With {
                        .Id = a.Id, .SubjectName = a.SubjectName, .ActivityType = a.ActivityType,
                        .Description = a.Description, .PerformedBy = a.PerformedBy, .CreatedAt = a.CreatedAt
                    }
                End Function),
            .Settings = Db.Query(Of SettingRow)(conn, tx, "SELECT setting_key, setting_value FROM settings ORDER BY setting_key",
                Function(r) New SettingRow With {.Key = Db.GetString(r, "setting_key"), .Value = Db.GetString(r, "setting_value")})
        }
    End Function

    ' ================= Restore =================

    ''' <summary>Reads a backup file. Throws BusinessRuleException when the file is not a readable backup.</summary>
    Public Shared Function ReadBackup(filePath As String) As BackupFile
        Dim info As New IO.FileInfo(filePath)
        If Not info.Exists Then Throw New BusinessRuleException("The selected backup file was not found.")
        If info.Length = 0 Then Throw New BusinessRuleException("The selected file is empty.")
        If info.Length > MaxFileBytes Then Throw New BusinessRuleException("The selected file is too large to be a PawStock backup.")
        Try
            Dim file = JsonSerializer.Deserialize(Of BackupFile)(IO.File.ReadAllText(filePath))
            If file Is Nothing Then Throw New BusinessRuleException("The selected file is not a PawStock backup.")
            Return file
        Catch ex As JsonException
            Throw New BusinessRuleException("The selected file is not a valid PawStock backup (the file could not be read as backup data).")
        End Try
    End Function

    ''' <summary>Full structural and referential validation. Returns a list of problems (empty when the file is safe to restore).</summary>
    Public Shared Function Validate(file As BackupFile) As List(Of String)
        Dim errors As New List(Of String)
        If file Is Nothing Then
            errors.Add("The file does not contain backup data.")
            Return errors
        End If
        If file.AppId <> AppId Then
            errors.Add("This file was not created by PawStock.")
            Return errors
        End If
        If file.FormatVersion <> FormatVersion Then
            errors.Add($"Unsupported backup format version ({file.FormatVersion}).")
            Return errors
        End If
        Dim d = file.Data
        If d Is Nothing OrElse d.Categories Is Nothing OrElse d.Units Is Nothing OrElse d.Suppliers Is Nothing OrElse
           d.Items Is Nothing OrElse d.StockTransactions Is Nothing OrElse d.Activities Is Nothing OrElse d.Settings Is Nothing Then
            errors.Add("The backup is missing one or more data sections.")
            Return errors
        End If
        If String.IsNullOrEmpty(file.Checksum) OrElse
           Not String.Equals(file.Checksum, ComputeChecksum(d), StringComparison.OrdinalIgnoreCase) Then
            errors.Add("The backup has been modified or is corrupted (checksum mismatch).")
            Return errors
        End If

        CheckLookups(errors, d.Categories, "Category")
        CheckLookups(errors, d.Units, "Unit")

        CheckUnique(errors, d.Suppliers.Select(Function(s) s.Id), "supplier id")
        CheckUniqueNames(errors, d.Suppliers.Select(Function(s) s.Name), "supplier name")
        For Each s In d.Suppliers
            If IsBlank(s.Name) OrElse IsBlank(s.ContactPerson) OrElse IsBlank(s.Phone) OrElse IsBlank(s.Address) Then
                errors.Add($"Supplier #{s.Id} is missing required information.")
            End If
        Next

        Dim categoryIds = d.Categories.Select(Function(c) c.Id).ToHashSet()
        Dim unitIds = d.Units.Select(Function(u) u.Id).ToHashSet()
        Dim supplierIds = d.Suppliers.Select(Function(s) s.Id).ToHashSet()
        CheckUnique(errors, d.Items.Select(Function(i) i.Id), "item id")
        CheckUniqueNames(errors, d.Items.Select(Function(i) i.Name), "item name")
        For Each it In d.Items
            If IsBlank(it.Name) Then errors.Add($"Item #{it.Id} has no name.")
            If it.Quantity < 0 OrElse it.Quantity > InputValidator.MaxAmount Then errors.Add($"Item ""{it.Name}"" has an invalid quantity.")
            If it.RetailPrice < 0 OrElse it.RetailPrice > InputValidator.MaxAmount Then errors.Add($"Item ""{it.Name}"" has an invalid retail price.")
            If Not categoryIds.Contains(it.CategoryId) Then errors.Add($"Item ""{it.Name}"" refers to a missing category.")
            If it.UnitId.HasValue AndAlso Not unitIds.Contains(it.UnitId.Value) Then errors.Add($"Item ""{it.Name}"" refers to a missing unit.")
            If Not supplierIds.Contains(it.SupplierId) Then errors.Add($"Item ""{it.Name}"" refers to a missing supplier.")
            If it.PetType IsNot Nothing AndAlso Not PetTypes.IsValid(it.PetType) Then errors.Add($"Item ""{it.Name}"" has an invalid pet type.")
            If it.Description IsNot Nothing AndAlso it.Description.Length > InventoryService.MaxDescriptionLength Then
                errors.Add($"Item ""{it.Name}"" has a description that is too long.")
            End If
            If it.Unit IsNot Nothing AndAlso it.Unit.Length > MeasurementUnits.MaxLength Then errors.Add($"Item ""{it.Name}"" has a unit that is too long.")
        Next

        Dim itemIds = d.Items.Select(Function(i) i.Id).ToHashSet()
        CheckUnique(errors, d.StockTransactions.Select(Function(t) t.Id), "stock record id")
        For Each t In d.StockTransactions
            If IsBlank(t.ItemName) OrElse Not TxnTypes.All.Contains(t.TxnType) Then errors.Add($"Stock record #{t.Id} is incomplete.")
            If t.QtyBefore < 0 OrElse t.QtyAfter < 0 Then errors.Add($"Stock record #{t.Id} has a negative quantity.")
            If t.ItemId.HasValue AndAlso Not itemIds.Contains(t.ItemId.Value) Then errors.Add($"Stock record #{t.Id} refers to a missing item.")
            If t.SupplierId.HasValue AndAlso Not supplierIds.Contains(t.SupplierId.Value) Then errors.Add($"Stock record #{t.Id} refers to a missing supplier.")
            If t.Unit IsNot Nothing AndAlso t.Unit.Length > MeasurementUnits.MaxLength Then errors.Add($"Stock record #{t.Id} has a unit that is too long.")
        Next

        CheckUnique(errors, d.Activities.Select(Function(a) a.Id), "activity id")
        For Each a In d.Activities
            If IsBlank(a.SubjectName) OrElse IsBlank(a.ActivityType) OrElse a.Description Is Nothing Then errors.Add($"Activity #{a.Id} is incomplete.")
        Next

        CheckUniqueNames(errors, d.Settings.Select(Function(s) s.Key), "setting")
        For Each s In d.Settings
            If IsBlank(s.Key) OrElse s.Value Is Nothing Then errors.Add("A setting entry is incomplete.")
        Next
        Dim low = SettingInt(d.Settings, SettingsRepository.LowStockKey, AppSettings.DefaultLowThreshold)
        Dim critical = SettingInt(d.Settings, SettingsRepository.CriticalStockKey, AppSettings.DefaultCriticalThreshold)
        Dim days = SettingInt(d.Settings, SettingsRepository.ExpiringSoonKey, AppSettings.DefaultExpiringSoonDays)
        If Not low.HasValue OrElse Not critical.HasValue OrElse Not days.HasValue OrElse
           StatusCalculator.ValidateRules(low.Value, critical.Value, days.Value).Count > 0 Then
            errors.Add("The stock status rules in the backup are invalid.")
        End If

        Return errors.Distinct().Take(15).ToList()
    End Function

    ''' <summary>Replaces all data with the backup. Returns the path of the automatic safety backup.</summary>
    Public Shared Function Restore(file As BackupFile) As String
        Dim errors = Validate(file)
        If errors.Count > 0 Then Throw New BusinessRuleException("The backup file is not valid: " & errors.First())

        Dim safetyPath = IO.Path.Combine(EnsureDefaultFolder(), SuggestedFileName("auto-before-restore"))
        CreateBackup(safetyPath, logActivity:=False)

        Dim d = file.Data
        Using conn = Db.Open()
            ' Rows are re-inserted with their original ids; FK checks are off only for this session while the
            ' tables are cleared and refilled. The data was already validated for referential integrity above.
            Db.Execute(conn, Nothing, "SET FOREIGN_KEY_CHECKS = 0")
            Try
                Using tx = conn.BeginTransaction()
                    ' DELETE (not TRUNCATE) so everything stays inside the transaction and can roll back.
                    For Each table In {"activity_log", "stock_transactions", "items", "suppliers", "units", "categories", "settings"}
                        Db.Execute(conn, tx, $"DELETE FROM {table}")
                    Next

                    For Each c In d.Categories
                        Db.Execute(conn, tx, "INSERT INTO categories (id, name) VALUES (@id, @name)", Db.P("@id", c.Id), Db.P("@name", c.Name))
                    Next
                    For Each u In d.Units
                        Db.Execute(conn, tx, "INSERT INTO units (id, name) VALUES (@id, @name)", Db.P("@id", u.Id), Db.P("@name", u.Name))
                    Next
                    For Each s In d.Suppliers
                        Db.Execute(conn, tx,
                            "INSERT INTO suppliers (id, name, contact_person, phone, address, email, is_sample, created_at, updated_at) " &
                            "VALUES (@id, @name, @contact, @phone, @address, @email, @sample, @created, @updated)",
                            Db.P("@id", s.Id), Db.P("@name", s.Name), Db.P("@contact", s.ContactPerson), Db.P("@phone", s.Phone),
                            Db.P("@address", s.Address), Db.P("@email", Db.NullIfBlank(s.Email)), Db.P("@sample", s.IsSample),
                            Db.P("@created", s.CreatedAt), Db.P("@updated", s.UpdatedAt))
                    Next
                    Dim unitNames = d.Units.ToDictionary(Function(u) u.Id, Function(u) u.Name)
                    For Each it In d.Items
                        ' A backup from before descriptions replaced units has no description: keep its old unit as the description.
                        Dim description = it.Description
                        If String.IsNullOrWhiteSpace(description) AndAlso it.UnitId.HasValue Then unitNames.TryGetValue(it.UnitId.Value, description)
                        Db.Execute(conn, tx,
                            "INSERT INTO items (id, name, category_id, pet_type, quantity, unit, unit_id, retail_price, supplier_id, expiration_date, description, is_sample, created_at, updated_at) " &
                            "VALUES (@id, @name, @category, @petType, @quantity, @measure, @unit, @price, @supplier, @expiration, @description, @sample, @created, @updated)",
                            Db.P("@id", it.Id), Db.P("@name", it.Name), Db.P("@category", it.CategoryId), Db.P("@petType", If(it.PetType, PetTypes.Both)),
                            Db.P("@quantity", it.Quantity), Db.P("@measure", Db.NullIfBlank(it.Unit)),
                            Db.P("@unit", it.UnitId), Db.P("@price", it.RetailPrice), Db.P("@supplier", it.SupplierId),
                            Db.P("@expiration", it.ExpirationDate), Db.P("@description", Db.NullIfBlank(description)), Db.P("@sample", it.IsSample),
                            Db.P("@created", it.CreatedAt), Db.P("@updated", it.UpdatedAt))
                    Next
                    For Each t In d.StockTransactions
                        Db.Execute(conn, tx,
                            "INSERT INTO stock_transactions (id, item_id, item_name, txn_type, qty_change, qty_before, qty_after, unit, supplier_id, supplier_name, txn_date, reason, note, created_at) " &
                            "VALUES (@id, @item, @itemName, @type, @change, @before, @after, @unit, @supplier, @supplierName, @date, @reason, @note, @created)",
                            Db.P("@id", t.Id), Db.P("@item", t.ItemId), Db.P("@itemName", t.ItemName), Db.P("@type", t.TxnType),
                            Db.P("@change", t.QtyChange), Db.P("@before", t.QtyBefore), Db.P("@after", t.QtyAfter), Db.P("@unit", Db.NullIfBlank(t.Unit)),
                            Db.P("@supplier", t.SupplierId), Db.P("@supplierName", t.SupplierName), Db.P("@date", t.TxnDate.Date),
                            Db.P("@reason", t.Reason), Db.P("@note", t.Note), Db.P("@created", t.CreatedAt))
                    Next
                    For Each a In d.Activities
                        Db.Execute(conn, tx,
                            "INSERT INTO activity_log (id, subject_name, activity_type, description, performed_by, created_at) " &
                            "VALUES (@id, @subject, @type, @description, @by, @created)",
                            Db.P("@id", a.Id), Db.P("@subject", a.SubjectName), Db.P("@type", a.ActivityType),
                            Db.P("@description", a.Description), Db.P("@by", a.PerformedBy), Db.P("@created", a.CreatedAt))
                    Next
                    For Each s In d.Settings
                        Db.Execute(conn, tx, "INSERT INTO settings (setting_key, setting_value) VALUES (@key, @value)",
                                   Db.P("@key", s.Key), Db.P("@value", s.Value))
                    Next

                    DatabaseInitializer.EnsureDefaults(conn, tx)
                    ActivityRepository.Log(conn, tx, "Backup from " & Fmt.DateTimeText(file.CreatedAt), ActivityTypes.DataRestored,
                        $"Restored {Fmt.Plural(d.Items.Count, "item")}, {Fmt.Plural(d.Suppliers.Count, "supplier")} and " &
                        $"{Fmt.Plural(d.StockTransactions.Count, "stock record")}. Previous data saved to {safetyPath}")
                    tx.Commit()
                End Using
            Finally
                Db.Execute(conn, Nothing, "SET FOREIGN_KEY_CHECKS = 1")
            End Try
        End Using

        AppEvents.RaiseDataChanged()
        Return safetyPath
    End Function

    ' ---- validation helpers ----

    Private Shared Function IsBlank(value As String) As Boolean
        Return String.IsNullOrWhiteSpace(value)
    End Function

    Private Shared Sub CheckLookups(errors As List(Of String), rows As List(Of LookupRow), label As String)
        CheckUnique(errors, rows.Select(Function(r) r.Id), label.ToLowerInvariant() & " id")
        CheckUniqueNames(errors, rows.Select(Function(r) r.Name), label.ToLowerInvariant() & " name")
        If rows.Any(Function(r) IsBlank(r.Name)) Then errors.Add($"A {label.ToLowerInvariant()} has no name.")
    End Sub

    Private Shared Sub CheckUnique(errors As List(Of String), ids As IEnumerable(Of Integer), label As String)
        Dim list = ids.ToList()
        If list.Distinct().Count() <> list.Count Then errors.Add($"Duplicate {label} found.")
        If list.Any(Function(i) i <= 0) Then errors.Add($"Invalid {label} found.")
    End Sub

    Private Shared Sub CheckUniqueNames(errors As List(Of String), names As IEnumerable(Of String), label As String)
        Dim list = names.Where(Function(n) n IsNot Nothing).Select(Function(n) n.Trim()).ToList()
        If list.Distinct(StringComparer.OrdinalIgnoreCase).Count() <> list.Count Then errors.Add($"Duplicate {label} found.")
    End Sub

    Private Shared Function SettingInt(settings As List(Of SettingRow), key As String, fallback As Integer) As Integer?
        Dim row = settings.FirstOrDefault(Function(s) s.Key = key)
        If row Is Nothing Then Return fallback
        Dim value As Integer
        If Integer.TryParse(row.Value, value) Then Return value
        Return Nothing
    End Function
End Class
