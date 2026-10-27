Imports MySqlConnector
Imports Xunit

''' <summary>Runs only when PAWSTOCK_TEST_DB=1 (requires MySQL/MariaDB on localhost:3306, user root, no password).</summary>
Public Class IntegrationFactAttribute
    Inherits FactAttribute

    Public Sub New()
        If Environment.GetEnvironmentVariable("PAWSTOCK_TEST_DB") <> "1" Then
            Skip = "Set PAWSTOCK_TEST_DB=1 with MySQL running (XAMPP) to run database integration tests."
        End If
    End Sub
End Class

''' <summary>
''' End-to-end checks of the services against a real, throwaway database
''' (pawstock_integration_test), covering the functional requirements.
''' </summary>
<Collection("Database")>
Public Class DatabaseIntegrationTests
    Implements IDisposable

    Private Const TestDatabase As String = "pawstock_integration_test"

    ''' <summary>Points the app at a throwaway database. PAWSTOCK_TEST_PORT overrides the default 3306.</summary>
    Friend Shared Sub ResetDatabase(Optional database As String = TestDatabase)
        Dim port = 3306
        Integer.TryParse(Environment.GetEnvironmentVariable("PAWSTOCK_TEST_PORT"), port)
        If port = 0 Then port = 3306
        Db.Config = New DbConfig With {.Database = database, .Port = port}
        BackupService.DefaultFolder = IO.Path.Combine(IO.Path.GetTempPath(), "pawstock-tests", "safety-backups")
        DropDatabase(database)
        DatabaseInitializer.Initialize()
    End Sub

    ''' <summary>Removes a throwaway test database so nothing is left on the developer's MySQL server.</summary>
    Friend Shared Sub DropDatabase(database As String)
        If Environment.GetEnvironmentVariable("PAWSTOCK_TEST_DB") <> "1" Then Return
        Using server As New MySqlConnection(Db.BuildConnectionString(False))
            server.Open()
            Db.Execute(server, Nothing, $"DROP DATABASE IF EXISTS `{database}`")
        End Using
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        DropDatabase(TestDatabase)
    End Sub

    Private Shared Function LookupId(kind As LookupKind, name As String) As Integer
        Return LookupService.GetAll(kind).First(Function(l) l.Name = name).Id
    End Function

    Private Shared Function FindItem(name As String) As Item
        Return InventoryService.GetItems().Single(Function(i) i.Name = name)
    End Function

    <IntegrationFact>
    Public Sub FullInventoryWorkflow()
        ResetDatabase()

        ' Initializer is idempotent and seeds lookups + default settings.
        DatabaseInitializer.Initialize()
        Dim categories = LookupService.GetAll(LookupKind.Category)
        Assert.Equal(9, categories.Count)
        Assert.Contains(categories, Function(c) c.Name = ItemCategories.Medicine)
        Assert.Contains(categories, Function(c) c.Name = ItemCategories.Vitamins)
        Assert.DoesNotContain(categories, Function(c) c.Name = ItemCategories.LegacyMedicineAndVitamins)
        Assert.Contains(LookupService.GetAll(LookupKind.Unit), Function(u) u.Name = "ml")
        Dim settings = SettingsService.GetSettings()
        Assert.Equal(10, settings.LowStockThreshold)
        Assert.Equal(5, settings.CriticalStockThreshold)

        ' ---- Suppliers ----
        Dim badPhone = Assert.Throws(Of InputValidationException)(
            Sub() SupplierService.Add(New Supplier With {.Name = "X", .ContactPerson = "Y", .Phone = "abc", .Address = "Z"}))
        Assert.True(badPhone.Errors.ContainsKey("phone"))

        Dim supplierId = SupplierService.Add(New Supplier With {
            .Name = "Aparri Pet Trading", .ContactPerson = "Maria Santos", .Phone = "0917 123 4567", .Address = "Centro, Aparri, Cagayan"})
        Assert.Throws(Of InputValidationException)(
            Sub() SupplierService.Add(New Supplier With {.Name = "aparri pet trading", .ContactPerson = "A", .Phone = "09171234567", .Address = "B"}))

        ' ---- Add items ----
        Dim shampoo As New Item With {
            .Name = "Oatmeal Shampoo 500ml", .CategoryId = LookupId(LookupKind.Category, "Shampoo & Conditioner"), .PetType = PetTypes.Both,
            .SupplierId = supplierId, .Quantity = 12D, .RetailPrice = 285.5D}
        InventoryService.AddItem(shampoo)
        Assert.Equal(StockStatus.InStock, FindItem(shampoo.Name).Status)
        Assert.Equal(ExpirationStatus.None, FindItem(shampoo.Name).ExpiryStatus)

        Dim duplicate = Assert.Throws(Of InputValidationException)(
            Sub() InventoryService.AddItem(New Item With {.Name = "OATMEAL SHAMPOO 500ML", .CategoryId = shampoo.CategoryId, .PetType = PetTypes.Both,
                                                               .SupplierId = supplierId}))
        Assert.True(duplicate.Errors.ContainsKey("name"))

        Dim clippers As New Item With {
            .Name = "Nail Clipper", .CategoryId = LookupId(LookupKind.Category, "Grooming Tools"), .PetType = PetTypes.Dog,
            .SupplierId = supplierId, .Quantity = 0D, .RetailPrice = 350D,
            .Description = "Heavy-duty clipper for large breeds"}
        InventoryService.AddItem(clippers)
        Assert.Equal(StockStatus.OutOfStock, FindItem("Nail Clipper").Status)
        Assert.Equal(PetTypes.Dog, FindItem("Nail Clipper").PetType)
        Assert.Equal("Heavy-duty clipper for large breeds", FindItem("Nail Clipper").Description)

        ' ---- Description: any text is saved as typed, edits persist, and used values come back as suggestions ----
        Dim custom = FindItem(shampoo.Name)
        custom.Description = "  15 g Pink  "
        Assert.True(InventoryService.UpdateItem(custom))
        Assert.Equal("15 g Pink", FindItem(shampoo.Name).Description)
        Dim suggestions = InventoryService.GetDescriptionSuggestions()
        Assert.Contains("15 g Pink", suggestions)
        Assert.Contains("Lamb", suggestions)
        Assert.Equal(suggestions.Count, suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        custom = FindItem(shampoo.Name)
        custom.Description = ""
        Assert.True(InventoryService.UpdateItem(custom))
        Assert.Null(FindItem(shampoo.Name).Description)

        Dim vitamins As New Item With {
            .Name = "Cat Vitamin Gel", .CategoryId = LookupId(LookupKind.Category, ItemCategories.Vitamins), .PetType = PetTypes.Cat,
            .SupplierId = supplierId, .Quantity = 0D, .RetailPrice = 310D}
        InventoryService.AddItem(vitamins)
        Assert.Equal(ItemCategories.Vitamins, FindItem(vitamins.Name).CategoryName)
        Assert.Equal(PetTypes.Cat, FindItem(vitamins.Name).PetType)
        Dim catEdit = FindItem(vitamins.Name)
        catEdit.PetType = PetTypes.Both
        Assert.True(InventoryService.UpdateItem(catEdit))
        Assert.Equal(PetTypes.Both, FindItem(vitamins.Name).PetType)
        Assert.Contains("pet type", ActivityService.GetRecent(1).Single().Description)
        InventoryService.DeleteItem(FindItem(vitamins.Name).Id)

        ' ---- Stock in ----
        StockService.StockIn(FindItem("Nail Clipper").Id, 5D, Date.Today, supplierId, "DR #1001")
        Assert.Equal(5D, FindItem("Nail Clipper").Quantity)
        Assert.Equal(StockStatus.CriticalStock, FindItem("Nail Clipper").Status)
        Dim future = Assert.Throws(Of InputValidationException)(
            Sub() StockService.StockIn(FindItem("Nail Clipper").Id, 1D, Date.Today.AddDays(1), Nothing, Nothing))
        Assert.True(future.Errors.ContainsKey("date"))

        ' ---- Adjustments ----
        Dim shampooId = FindItem(shampoo.Name).Id
        Assert.Throws(Of InputValidationException)(Sub() StockService.Adjust(shampooId, AdjustmentMode.Deduct, 3D, Nothing, Nothing))
        Assert.Throws(Of InputValidationException)(Sub() StockService.Adjust(shampooId, AdjustmentMode.Deduct, 50D, "Damaged", Nothing))
        Assert.Equal(9D, StockService.Adjust(shampooId, AdjustmentMode.Deduct, 3D, "Damaged", "Dropped bottles"))
        Assert.Equal(StockStatus.LowStock, FindItem(shampoo.Name).Status)

        Dim stockIns = StockService.GetHistory(adjustments:=False)
        Assert.Equal(2, stockIns.Count) ' opening stock of the shampoo + one delivery (clipper started at 0)
        Dim adjustmentsHistory = StockService.GetHistory(adjustments:=True)
        Assert.Single(adjustmentsHistory)
        Assert.Equal(-3D, adjustmentsHistory(0).QtyChange)

        ' ---- Thresholds change statuses everywhere ----
        Assert.Throws(Of InputValidationException)(Sub() SettingsService.SaveStockRules(5, 5, 30))
        SettingsService.SaveStockRules(20, 10, 30)
        Assert.Equal(StockStatus.CriticalStock, FindItem(shampoo.Name).Status)
        SettingsService.SaveStockRules(10, 5, 30)

        ' ---- Expiration ----
        Dim edited = FindItem(shampoo.Name)
        edited.ExpirationDate = Date.Today.AddDays(5)
        Assert.True(InventoryService.UpdateItem(edited))
        Assert.Equal(ExpirationStatus.ExpiringSoon, FindItem(shampoo.Name).ExpiryStatus)
        Assert.False(InventoryService.UpdateItem(FindItem(shampoo.Name)))   ' no changes
        Dim expired = FindItem("Nail Clipper")
        expired.ExpirationDate = Date.Today.AddDays(-2)
        InventoryService.UpdateItem(expired)
        Dim summary = InventoryService.LoadSnapshot().Summary
        Assert.Equal(2, summary.TotalItems)
        Assert.Equal(14D, summary.AvailableStock)
        Assert.Equal(1, summary.ExpiringSoon)
        Assert.Equal(1, summary.Expired)
        Assert.Equal(1, summary.LowStock)
        Assert.Equal(1, summary.CriticalStock)

        ' ---- Supplier rename shows on items; delete blocked while in use ----
        Dim supplier = SupplierService.GetAll().Single()
        supplier.Name = "Aparri Pet Trading Co."
        Assert.True(SupplierService.Update(supplier))
        Assert.Equal("Aparri Pet Trading Co.", FindItem(shampoo.Name).SupplierName)
        Assert.Throws(Of BusinessRuleException)(Sub() SupplierService.Delete(supplier.Id))

        ' ---- Categories in use cannot be removed ----
        Assert.Throws(Of BusinessRuleException)(Sub() LookupService.Remove(LookupKind.Category, shampoo.CategoryId))
        Assert.Throws(Of InputValidationException)(Sub() LookupService.Add(LookupKind.Category, "grooming tools"))

        ' ---- Activities ----
        Dim types = ActivityService.GetTypes()
        For Each expectedType In {ActivityTypes.ItemAdded, ActivityTypes.ItemUpdated, ActivityTypes.StockReceived,
                                  ActivityTypes.StockAdjusted, ActivityTypes.SupplierAdded, ActivityTypes.SupplierUpdated}
            Assert.Contains(expectedType, types)
        Next
        Assert.NotEmpty(ActivityService.Search(Date.Today, Date.Today, ActivityTypes.StockReceived, "Nail", 100))

        ' ---- Backup → change → restore ----
        Dim folder = IO.Path.Combine(IO.Path.GetTempPath(), "pawstock-tests")
        IO.Directory.CreateDirectory(folder)
        Dim backupPath = IO.Path.Combine(folder, "backup.json")
        Dim result = BackupService.CreateBackup(backupPath)
        Assert.Equal(2, result.ItemCount)
        Assert.Empty(BackupService.Validate(BackupService.ReadBackup(backupPath)))

        InventoryService.DeleteItem(FindItem("Nail Clipper").Id)
        Assert.Single(InventoryService.GetItems())
        Assert.Contains(ActivityService.GetRecent(1), Function(a) a.ActivityType = ActivityTypes.ItemRemoved)

        Dim safety = BackupService.Restore(BackupService.ReadBackup(backupPath))
        Assert.True(IO.File.Exists(safety))
        Dim restored = InventoryService.GetItems()
        Assert.Equal(2, restored.Count)
        Assert.Equal(9D, restored.Single(Function(i) i.Name = shampoo.Name).Quantity)
        Assert.Equal(Date.Today.AddDays(-2), restored.Single(Function(i) i.Name = "Nail Clipper").ExpirationDate)
        Assert.Equal(PetTypes.Dog, restored.Single(Function(i) i.Name = "Nail Clipper").PetType)
        Assert.Equal("Heavy-duty clipper for large breeds", restored.Single(Function(i) i.Name = "Nail Clipper").Description)
        Assert.Equal(2, StockService.GetHistory(adjustments:=False).Count)
        Assert.Equal(ActivityTypes.DataRestored, ActivityService.GetRecent(1).Single().ActivityType)

        ' New rows after a restore get fresh ids (AUTO_INCREMENT continues correctly).
        InventoryService.AddItem(New Item With {.Name = "Cotton Balls", .CategoryId = shampoo.CategoryId, .PetType = PetTypes.Both,
                                                .SupplierId = supplier.Id, .Quantity = 40D, .RetailPrice = 45D})
        Assert.Equal(3, InventoryService.GetItems().Count)

        ' Tampered and non-backup files are rejected without touching data.
        Dim tamperedPath = IO.Path.Combine(folder, "tampered.json")
        IO.File.WriteAllText(tamperedPath, IO.File.ReadAllText(backupPath).Replace("""Quantity"": 9.00", """Quantity"": 900.00"))
        Assert.NotEmpty(BackupService.Validate(BackupService.ReadBackup(tamperedPath)))
        Dim junkPath = IO.Path.Combine(folder, "junk.json")
        IO.File.WriteAllText(junkPath, "this is not json")
        Assert.Throws(Of BusinessRuleException)(Sub() BackupService.ReadBackup(junkPath))
        Assert.Equal(3, InventoryService.GetItems().Count)

        ' ---- Sample data ----
        SampleDataService.Load()
        Dim withSamples = InventoryService.GetItems()
        Assert.True(withSamples.Where(Function(i) i.IsSample).Count() >= 10)
        Assert.Contains(withSamples, Function(i) i.IsSample AndAlso i.Status = StockStatus.OutOfStock)
        Assert.Contains(withSamples, Function(i) i.IsSample AndAlso i.ExpiryStatus = ExpirationStatus.Expired)
        Assert.Throws(Of BusinessRuleException)(Sub() SampleDataService.Load())
        SampleDataService.Remove()
        Assert.Equal(3, InventoryService.GetItems().Count)
        Assert.DoesNotContain(SupplierService.GetAll(), Function(s) s.IsSample)
    End Sub

    <IntegrationFact>
    Public Sub Initialize_UpgradesDatabaseFromBeforePetTypes()
        ResetDatabase()

        ' Recreate what an older install looks like: no pet_type / description columns and the combined category in use.
        Using conn = Db.Open()
            Db.Execute(conn, Nothing, "ALTER TABLE items DROP CONSTRAINT chk_items_pet_type")
            Db.Execute(conn, Nothing, "ALTER TABLE items DROP COLUMN pet_type, DROP COLUMN description")
            Db.Execute(conn, Nothing, "ALTER TABLE items MODIFY COLUMN unit_id INT NOT NULL")   ' units were required back then
            Dim legacyId = Db.Insert(conn, Nothing, "INSERT INTO categories (name) VALUES (@name)", Db.P("@name", ItemCategories.LegacyMedicineAndVitamins))
            Dim supplierId = Db.Insert(conn, Nothing,
                "INSERT INTO suppliers (name, contact_person, phone, address) VALUES ('Old Supplier', 'Ana', '09171234567', 'Aparri')")
            Dim unitId = Convert.ToInt32(Db.Scalar(conn, Nothing, "SELECT id FROM units WHERE name = 'bottle'"))
            Db.Execute(conn, Nothing,
                "INSERT INTO items (name, category_id, quantity, unit_id, retail_price, supplier_id) VALUES ('Ear Drops', @category, 3, @unit, 150, @supplier)",
                Db.P("@category", legacyId), Db.P("@unit", unitId), Db.P("@supplier", supplierId))
        End Using

        DatabaseInitializer.Initialize()

        Dim item = FindItem("Ear Drops")
        Assert.Equal(ItemCategories.Medicine, item.CategoryName)
        Assert.Equal(PetTypes.Both, item.PetType)
        Assert.Equal("bottle", item.Description)   ' the old unit carries over as the description
        Dim categories = LookupService.GetAll(LookupKind.Category)
        Assert.DoesNotContain(categories, Function(c) c.Name = ItemCategories.LegacyMedicineAndVitamins)
        Assert.Single(categories, Function(c) c.Name = ItemCategories.Medicine)
        Assert.Contains(categories, Function(c) c.Name = ItemCategories.Vitamins)
    End Sub

    <IntegrationFact>
    Public Sub Initialize_ReplacesUnitWithDescription_WithoutLosingData()
        ResetDatabase()

        Using conn = Db.Open()
            Db.Execute(conn, Nothing, "ALTER TABLE items MODIFY COLUMN unit_id INT NOT NULL")   ' an install from before descriptions replaced units
            Dim categoryId = Convert.ToInt32(Db.Scalar(conn, Nothing, "SELECT id FROM categories WHERE name = 'Accessories'"))
            Dim supplierId = Db.Insert(conn, Nothing,
                "INSERT INTO suppliers (name, contact_person, phone, address) VALUES ('Old Supplier', 'Ana', '09171234567', 'Aparri')")
            Dim mlId = Convert.ToInt32(Db.Scalar(conn, Nothing, "SELECT id FROM units WHERE name = 'ml'"))
            Dim bagId = Convert.ToInt32(Db.Scalar(conn, Nothing, "SELECT id FROM units WHERE name = 'bag'"))
            For Each row In {("No Description", mlId, CType(Nothing, String)), ("Blank Description", mlId, "   "), ("Has Notes", bagId, "For puppies 8 weeks and up")}
                Db.Execute(conn, Nothing,
                    "INSERT INTO items (name, category_id, quantity, unit_id, retail_price, supplier_id, description) VALUES (@name, @category, 2, @unit, 10, @supplier, @description)",
                    Db.P("@name", row.Item1), Db.P("@category", categoryId), Db.P("@unit", row.Item2), Db.P("@supplier", supplierId), Db.P("@description", row.Item3))
            Next
        End Using

        DatabaseInitializer.Initialize()

        Assert.Equal("ml", FindItem("No Description").Description)
        Assert.Equal("ml", FindItem("Blank Description").Description)
        Assert.Equal("For puppies 8 weeks and up", FindItem("Has Notes").Description)   ' existing text is never overwritten
        Using conn = Db.Open()
            Assert.Equal(0, Db.Count(conn, Nothing, "SELECT COUNT(*) FROM items WHERE unit_id IS NULL"))   ' old links are kept
            Assert.Equal(3, Db.Count(conn, Nothing, "SELECT COUNT(*) FROM items"))
        End Using

        ' The upgrade runs once: clearing a description later is not undone on the next start-up.
        Dim edited = FindItem("No Description")
        edited.Description = ""
        InventoryService.UpdateItem(edited)
        DatabaseInitializer.Initialize()
        Assert.Null(FindItem("No Description").Description)

        ' New items need no unit at all.
        Dim supplier = SupplierService.GetAll().Single()
        InventoryService.AddItem(New Item With {.Name = "Brand New", .CategoryId = edited.CategoryId, .PetType = PetTypes.Cat, .SupplierId = supplier.Id,
                                                .Quantity = 1D, .RetailPrice = 5D, .Description = "30 tablets"})
        Assert.Equal("30 tablets", FindItem("Brand New").Description)
    End Sub
End Class
