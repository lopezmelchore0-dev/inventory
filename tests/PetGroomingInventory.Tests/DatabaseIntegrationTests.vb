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
            .SupplierId = supplierId, .Quantity = 12D, .Unit = "pcs", .RetailPrice = 285.5D}
        InventoryService.AddItem(shampoo)
        Assert.Equal(StockStatus.InStock, FindItem(shampoo.Name).Status)
        Assert.Equal(ExpirationStatus.None, FindItem(shampoo.Name).ExpiryStatus)

        Dim duplicate = Assert.Throws(Of InputValidationException)(
            Sub() InventoryService.AddItem(New Item With {.Name = "OATMEAL SHAMPOO 500ML", .CategoryId = shampoo.CategoryId, .PetType = PetTypes.Both,
                                                               .SupplierId = supplierId, .Unit = "pcs"}))
        Assert.True(duplicate.Errors.ContainsKey("name"))

        Dim clippers As New Item With {
            .Name = "Nail Clipper", .CategoryId = LookupId(LookupKind.Category, "Grooming Tools"), .PetType = PetTypes.Dog,
            .SupplierId = supplierId, .Quantity = 0D, .Unit = "pcs", .RetailPrice = 350D,
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
            .SupplierId = supplierId, .Quantity = 0D, .Unit = "g", .RetailPrice = 310D}
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
        StockService.StockIn(FindItem("Nail Clipper").Id, 5D, Date.Today, supplierId, "DR #1001", "pcs")
        Assert.Equal(5D, FindItem("Nail Clipper").Quantity)
        Assert.Equal(StockStatus.CriticalStock, FindItem("Nail Clipper").Status)
        Dim future = Assert.Throws(Of InputValidationException)(
            Sub() StockService.StockIn(FindItem("Nail Clipper").Id, 1D, Date.Today.AddDays(1), Nothing, Nothing, "pcs"))
        Assert.True(future.Errors.ContainsKey("date"))

        ' ---- Adjustments ----
        Dim shampooId = FindItem(shampoo.Name).Id
        Assert.Throws(Of InputValidationException)(Sub() StockService.Adjust(shampooId, AdjustmentMode.Deduct, 3D, Nothing, Nothing, "pcs"))
        Assert.Throws(Of InputValidationException)(Sub() StockService.Adjust(shampooId, AdjustmentMode.Deduct, 50D, "Damaged", Nothing, "pcs"))
        Assert.Equal(9D, StockService.Adjust(shampooId, AdjustmentMode.Deduct, 3D, "Damaged", "Dropped bottles", "pcs"))
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
                                                .SupplierId = supplier.Id, .Quantity = 40D, .Unit = "pack", .RetailPrice = 45D})
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
        ' The re-added column takes custom pet types too.
        Dim edit = FindItem("Ear Drops")
        edit.PetType = "Rabbit"
        edit.Unit = "drop"
        Assert.True(InventoryService.UpdateItem(edit))
        Assert.Equal("Rabbit", FindItem("Ear Drops").PetType)
    End Sub

    <IntegrationFact>
    Public Sub Initialize_LiftsTheDogCatOnlyLimit_AndKeepsExistingPetTypes()
        ResetDatabase()

        ' Recreate an install from when pet types were limited to Dog / Cat / Both.
        Dim supplierId = SupplierService.Add(New Supplier With {.Name = "Pet Supplier", .ContactPerson = "Ana", .Phone = "09171234567", .Address = "Aparri"})
        Dim categoryId = LookupId(LookupKind.Category, "Grooming Supplies")
        Using conn = Db.Open()
            Db.Execute(conn, Nothing, "ALTER TABLE items MODIFY COLUMN pet_type VARCHAR(4) NOT NULL DEFAULT 'Both'")
            Db.Execute(conn, Nothing, "ALTER TABLE items ADD CONSTRAINT chk_items_pet_type CHECK (pet_type IN ('Dog', 'Cat', 'Both'))")
            For Each pair In {("Dog Comb", "Dog"), ("Cat Comb", "Cat"), ("Shared Comb", "Both")}
                Db.Execute(conn, Nothing,
                    "INSERT INTO items (name, category_id, pet_type, quantity, unit, retail_price, supplier_id) VALUES (@name, @category, @pet, 1, 'pcs', 10, @supplier)",
                    Db.P("@name", pair.Item1), Db.P("@category", categoryId), Db.P("@pet", pair.Item2), Db.P("@supplier", supplierId))
            Next
            Assert.ThrowsAny(Of Exception)(Sub() Db.Execute(conn, Nothing, "UPDATE items SET pet_type = 'Bird' WHERE name = 'Dog Comb'"))
        End Using

        DatabaseInitializer.Initialize()
        DatabaseInitializer.Initialize()   ' idempotent

        Assert.Equal(PetTypes.Dog, FindItem("Dog Comb").PetType)
        Assert.Equal(PetTypes.Cat, FindItem("Cat Comb").PetType)
        Assert.Equal(PetTypes.Both, FindItem("Shared Comb").PetType)
        InventoryService.AddItem(New Item With {.Name = "Hamster Wheel", .CategoryId = categoryId, .PetType = "Guinea Pig", .SupplierId = supplierId,
                                                .Quantity = 1D, .Unit = "pcs", .RetailPrice = 250D})
        Assert.Equal("Guinea Pig", FindItem("Hamster Wheel").PetType)
    End Sub

    <IntegrationFact>
    Public Sub CustomPetTypes_AreSavedSuggestedAndFiltered()
        ResetDatabase()
        Dim supplierId = SupplierService.Add(New Supplier With {.Name = "Exotic Supplier", .ContactPerson = "Ana", .Phone = "09171234567", .Address = "Aparri"})
        Dim categoryId = LookupId(LookupKind.Category, "Grooming Supplies")
        Dim newItem = Function(name As String, petType As String) New Item With {
            .Name = name, .CategoryId = categoryId, .PetType = petType, .SupplierId = supplierId, .Quantity = 5D, .Unit = "pcs", .RetailPrice = 50D}

        ' A custom pet type is saved as typed (trimmed); the editor's labels are stored as the built-in values.
        InventoryService.AddItem(newItem("Rabbit Brush", "  Rabbit "))
        InventoryService.AddItem(newItem("Dog Shampoo", "dog"))
        InventoryService.AddItem(newItem("Shared Towel", PetTypes.BothLabel))
        Assert.Equal("Rabbit", FindItem("Rabbit Brush").PetType)
        Assert.Equal("Rabbit", FindItem("Rabbit Brush").PetTypeText)
        Assert.Equal(PetTypes.Dog, FindItem("Dog Shampoo").PetType)
        Assert.Equal(PetTypes.Both, FindItem("Shared Towel").PetType)
        Assert.Contains(ActivityService.GetRecent(3), Function(a) a.Description.Contains("· Rabbit, supplier"))   ' the activity log names the custom pet type

        ' The same pet type typed in another case reuses the saved spelling.
        InventoryService.AddItem(newItem("Rabbit Pellets", "rabbit"))
        Assert.Equal("Rabbit", FindItem("Rabbit Pellets").PetType)

        ' Suggestions and filters include it once; it is not a dog item.
        Dim suggestions = InventoryService.GetPetTypeSuggestions()
        Assert.Equal({"Dog", "Cat", PetTypes.BothLabel, PetTypes.OtherLabel, "Rabbit"}, suggestions)
        Assert.Equal({"Dog", "Cat", "Both", "Rabbit"}, InventoryService.GetPetTypesInUse())
        Dim items = InventoryService.GetItems()
        Assert.Equal({"Rabbit Brush", "Rabbit Pellets"}, items.Where(Function(i) PetTypes.Matches(i.PetType, "Rabbit")).Select(Function(i) i.Name).OrderBy(Function(n) n))
        Assert.DoesNotContain(items.Where(Function(i) PetTypes.Matches(i.PetType, PetTypes.Dog)), Function(i) i.PetType = "Rabbit")

        ' Editing: switch to another custom type, then back to a built-in one.
        Dim edit = FindItem("Rabbit Brush")
        edit.PetType = "Hamster"
        Assert.True(InventoryService.UpdateItem(edit))
        Assert.Equal("Hamster", FindItem("Rabbit Brush").PetType)
        Assert.Contains("pet type Rabbit → Hamster", ActivityService.GetRecent(1).Single().Description)
        edit = FindItem("Rabbit Brush")
        edit.PetType = "Cat"
        Assert.True(InventoryService.UpdateItem(edit))
        Assert.Equal(PetTypes.Cat, FindItem("Rabbit Brush").PetType)

        ' Blank and over-long pet types are refused.
        Dim blank = Assert.Throws(Of InputValidationException)(Sub() InventoryService.AddItem(newItem("No Pet", " ")))
        Assert.True(blank.Errors.ContainsKey("pettype"))
        Dim tooLong = Assert.Throws(Of InputValidationException)(Sub() InventoryService.AddItem(newItem("Long Pet", New String("x"c, PetTypes.MaxLength + 1))))
        Assert.True(tooLong.Errors.ContainsKey("pettype"))

        ' Backup and restore keep custom pet types.
        Dim path = IO.Path.Combine(IO.Path.GetTempPath(), $"pawstock-pettype-{Guid.NewGuid():N}.json")
        Try
            BackupService.CreateBackup(path)
            ResetDatabase()
            BackupService.Restore(BackupService.ReadBackup(path))
            Assert.Equal("Rabbit", FindItem("Rabbit Pellets").PetType)
        Finally
            IO.File.Delete(path)
        End Try
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
        edited.Unit = "ml"   ' items from before units need one when they are next edited
        InventoryService.UpdateItem(edited)
        DatabaseInitializer.Initialize()
        Assert.Null(FindItem("No Description").Description)

        ' New items need no legacy unit_id (their unit of measure lives in items.unit).
        Dim supplier = SupplierService.GetAll().Single()
        InventoryService.AddItem(New Item With {.Name = "Brand New", .CategoryId = edited.CategoryId, .PetType = PetTypes.Cat, .SupplierId = supplier.Id,
                                                .Quantity = 1D, .Unit = "pcs", .RetailPrice = 5D, .Description = "30 tablets"})
        Assert.Equal("30 tablets", FindItem("Brand New").Description)
    End Sub

    <IntegrationFact>
    Public Sub Initialize_AddsUnitColumns_WithoutChangingExistingData()
        ResetDatabase()
        Using conn = Db.Open()
            ' An install from before units of measure.
            Db.Execute(conn, Nothing, "ALTER TABLE items DROP COLUMN unit")
            Db.Execute(conn, Nothing, "ALTER TABLE stock_transactions DROP COLUMN unit")
            Dim categoryId = Convert.ToInt32(Db.Scalar(conn, Nothing, "SELECT id FROM categories WHERE name = 'Accessories'"))
            Dim supplierId = Db.Insert(conn, Nothing,
                "INSERT INTO suppliers (name, contact_person, phone, address) VALUES ('Old Supplier', 'Ana', '09171234567', 'Aparri')")
            Dim itemId = Db.Insert(conn, Nothing,
                "INSERT INTO items (name, category_id, quantity, retail_price, supplier_id, description) VALUES ('Old Collar', @category, 7.5, 60, @supplier, 'Red')",
                Db.P("@category", categoryId), Db.P("@supplier", supplierId))
            Db.Execute(conn, Nothing,
                "INSERT INTO stock_transactions (item_id, item_name, txn_type, qty_change, qty_before, qty_after, txn_date) VALUES (@id, 'Old Collar', 'INITIAL', 7.5, 0, 7.5, CURDATE())",
                Db.P("@id", itemId))
        End Using

        DatabaseInitializer.Initialize()
        DatabaseInitializer.Initialize()   ' idempotent

        Dim old = FindItem("Old Collar")
        Assert.Null(old.Unit)                      ' nothing is guessed
        Assert.Equal(7.5D, old.Quantity)
        Assert.Equal("Red", old.Description)
        Assert.Equal("7.5", old.QuantityText)      ' shown exactly as before
        Assert.Null(StockService.GetHistory(adjustments:=False).Single().Unit)
    End Sub

    <IntegrationFact>
    Public Sub Units_AreSavedAndUsedThroughoutTheStockWorkflow()
        ResetDatabase()
        Dim supplierId = SupplierService.Add(New Supplier With {.Name = "Unit Supplier", .ContactPerson = "Ana", .Phone = "09171234567", .Address = "Aparri"})
        Dim categoryId = LookupId(LookupKind.Category, "Grooming Supplies")
        Dim newItem = Function(name As String, quantity As Decimal, unit As String) New Item With {
            .Name = name, .CategoryId = categoryId, .PetType = PetTypes.Both, .SupplierId = supplierId, .Quantity = quantity, .Unit = unit, .RetailPrice = 10D}

        ' A dropdown label (or its plural) is stored as its short unit; a custom unit is stored as typed.
        InventoryService.AddItem(newItem("Gauze Pads", 10D, "Pieces"))
        InventoryService.AddItem(newItem("Coat Spray", 1.5D, "Gallon"))
        InventoryService.AddItem(newItem("Shampoo Sachet", 0D, "Sachets"))
        Assert.Equal("pcs", FindItem("Gauze Pads").Unit)
        Assert.Equal("10 pcs", FindItem("Gauze Pads").QuantityText)
        Assert.Equal("gallon", FindItem("Coat Spray").Unit)
        Assert.Equal("sachet", FindItem("Shampoo Sachet").Unit)
        Dim suggestions = InventoryService.GetUnitSuggestions()
        Assert.Single(suggestions, Function(s) s = "Sachet")
        Assert.Single(suggestions, Function(s) s = "Pieces")

        ' A unit is required, and counted units take whole numbers.
        Assert.True(Assert.Throws(Of InputValidationException)(Function() InventoryService.AddItem(newItem("No Unit", 1D, "Others"))).Errors.ContainsKey("unit"))
        Assert.True(Assert.Throws(Of InputValidationException)(Function() InventoryService.AddItem(newItem("Half Piece", 2.5D, "pcs"))).Errors.ContainsKey("quantity"))

        ' Stock in and adjustments are counted in the item's unit, and every stock record keeps it.
        Dim gauzeId = FindItem("Gauze Pads").Id
        StockService.StockIn(gauzeId, 5D, Date.Today, supplierId, Nothing, "pcs")
        Assert.Equal(13D, StockService.Adjust(gauzeId, AdjustmentMode.Deduct, 2D, "Damaged", Nothing, "pcs"))
        Assert.Equal(13D, FindItem("Gauze Pads").Quantity)
        Assert.True(Assert.Throws(Of InputValidationException)(Sub() StockService.StockIn(gauzeId, 0.5D, Date.Today, Nothing, Nothing, "pcs")).Errors.ContainsKey("quantity"))
        Assert.True(Assert.Throws(Of InputValidationException)(Function() StockService.Adjust(gauzeId, AdjustmentMode.Deduct, 1.5D, "Damaged", Nothing, "pcs")).Errors.ContainsKey("amount"))
        Assert.Equal(13D, FindItem("Gauze Pads").Quantity)   ' rejected changes leave stock untouched
        Dim sprayId = FindItem("Coat Spray").Id
        StockService.StockIn(sprayId, 0.75D, Date.Today, Nothing, Nothing, "Gallon")
        Assert.Equal(2.25D, FindItem("Coat Spray").Quantity)

        Using conn = Db.Open()
            Assert.Equal(3, Db.Count(conn, Nothing, "SELECT COUNT(*) FROM stock_transactions WHERE item_id = @id AND unit = 'pcs'", Db.P("@id", gauzeId)))
            Assert.Equal(2, Db.Count(conn, Nothing, "SELECT COUNT(*) FROM stock_transactions WHERE item_id = @id AND unit = 'gallon'", Db.P("@id", sprayId)))
        End Using
        Assert.Equal("−2 pcs", StockService.GetHistory(adjustments:=True).Single().ChangeText)
        Assert.Contains("Received 5 pcs", ActivityService.Search(Date.Today, Date.Today, ActivityTypes.StockReceived, "Gauze", 10).Single().Description)

        ' Changing the unit never converts the quantity, and fractional stock cannot become a counted unit.
        Dim spray = FindItem("Coat Spray")
        spray.Unit = "pcs"
        Assert.True(Assert.Throws(Of InputValidationException)(Function() InventoryService.UpdateItem(spray)).Errors.ContainsKey("unit"))
        spray.Unit = "ml"   ' a custom unit
        Assert.True(InventoryService.UpdateItem(spray))
        Assert.Equal("ml", FindItem("Coat Spray").Unit)
        Assert.Equal(2.25D, FindItem("Coat Spray").Quantity)
        Assert.Contains("unit gallon → ml", ActivityService.GetRecent(1).Single().Description)

        ' Dashboard total is kept per unit.
        Assert.Equal("2.25 ml · 13 pcs · 0 sachets", InventoryService.LoadSnapshot().Summary.StockByUnit)

        ' Backup → change → restore keeps every unit, including those on stock records.
        Dim folder = IO.Path.Combine(IO.Path.GetTempPath(), "pawstock-tests")
        IO.Directory.CreateDirectory(folder)
        Dim backupPath = IO.Path.Combine(folder, "units-backup.json")
        BackupService.CreateBackup(backupPath)
        Dim gauze = FindItem("Gauze Pads")
        gauze.Unit = "Box"
        Assert.True(InventoryService.UpdateItem(gauze))
        BackupService.Restore(BackupService.ReadBackup(backupPath))
        Assert.Equal("pcs", FindItem("Gauze Pads").Unit)
        Assert.Equal("ml", FindItem("Coat Spray").Unit)
        Using conn = Db.Open()
            Assert.Equal(2, Db.Count(conn, Nothing, "SELECT COUNT(*) FROM stock_transactions WHERE item_id = @id AND unit = 'gallon'", Db.P("@id", sprayId)))
        End Using

        ' Items from before units keep a blank unit: editing one asks for a unit, and their older stock records show the item's unit.
        Using conn = Db.Open()
            Db.Execute(conn, Nothing, "UPDATE items SET unit = NULL WHERE name = 'Shampoo Sachet'")
            Db.Execute(conn, Nothing, "UPDATE stock_transactions SET unit = NULL WHERE item_id = @id", Db.P("@id", gauzeId))
        End Using
        Dim legacy = FindItem("Shampoo Sachet")
        Assert.Equal("0", legacy.QuantityText)
        legacy.Description = "10 ml"
        Assert.True(Assert.Throws(Of InputValidationException)(Function() InventoryService.UpdateItem(legacy)).Errors.ContainsKey("unit"))
        Assert.All(StockService.GetHistory(adjustments:=False).Where(Function(t) t.ItemId.GetValueOrDefault() = gauzeId), Sub(t) Assert.Equal("pcs", t.Unit))

        ' A backup without any units (the format of earlier versions) has no unit fields and restores with blank units.
        Using conn = Db.Open()
            Db.Execute(conn, Nothing, "UPDATE items SET unit = NULL")
            Db.Execute(conn, Nothing, "UPDATE stock_transactions SET unit = NULL")
        End Using
        Dim oldStylePath = IO.Path.Combine(folder, "units-old-style.json")
        BackupService.CreateBackup(oldStylePath)
        Assert.DoesNotContain("""Unit"":", IO.File.ReadAllText(oldStylePath))
        Assert.Empty(BackupService.Validate(BackupService.ReadBackup(oldStylePath)))
        BackupService.Restore(BackupService.ReadBackup(oldStylePath))
        Assert.All(InventoryService.GetItems(), Sub(i) Assert.Null(i.Unit))
    End Sub

    <IntegrationFact>
    Public Sub StockChanges_AreRejectedWhenTheUnitDiffersFromTheItemsRegisteredUnit()
        ResetDatabase()
        Dim supplierId = SupplierService.Add(New Supplier With {.Name = "Bundle Supplier", .ContactPerson = "Ana", .Phone = "09171234567", .Address = "Aparri"})
        InventoryService.AddItem(New Item With {
            .Name = "Cotton Balls", .CategoryId = LookupId(LookupKind.Category, "Grooming Supplies"), .PetType = PetTypes.Both,
            .SupplierId = supplierId, .Quantity = 4D, .Unit = "Bundle", .RetailPrice = 20D})
        Dim itemId = FindItem("Cotton Balls").Id
        Dim countRecords = Function()
                               Using conn = Db.Open()
                                   Return Db.Count(conn, Nothing, "SELECT COUNT(*) FROM stock_transactions WHERE item_id = @id", Db.P("@id", itemId))
                               End Using
                           End Function

        ' The registered unit (given as its value or its dropdown label) is accepted and saved on the record.
        StockService.StockIn(itemId, 5D, Date.Today, supplierId, Nothing, "bundle")
        Assert.Equal(1D, StockService.Adjust(itemId, AdjustmentMode.Deduct, 8D, "Damaged", Nothing, "Bundle"))
        Assert.All(StockService.GetHistory(adjustments:=False).Concat(StockService.GetHistory(adjustments:=True)),
                   Sub(t) Assert.Equal("bundle", t.Unit))
        Assert.Equal("−8 bundles", StockService.GetHistory(adjustments:=True).Single().ChangeText)
        Assert.Equal("bundles", StockService.GetHistory(adjustments:=True).Single().UnitText)

        ' The unit changes after a form was opened with the old one: nothing is recorded in the wrong unit.
        Dim edited = FindItem("Cotton Balls")
        edited.Unit = "Box"
        Assert.True(InventoryService.UpdateItem(edited))
        Dim recordsBefore = countRecords()
        Dim stockIn = Assert.Throws(Of InputValidationException)(Sub() StockService.StockIn(itemId, 2D, Date.Today, Nothing, Nothing, "bundle"))
        Assert.True(stockIn.Errors.ContainsKey("unit"))
        Assert.Contains("boxes", stockIn.Errors("unit"))
        Dim adjust = Assert.Throws(Of InputValidationException)(Function() StockService.Adjust(itemId, AdjustmentMode.Add, 2D, "Count correction", Nothing, "bundle"))
        Assert.True(adjust.Errors.ContainsKey("unit"))
        Assert.Equal(1D, FindItem("Cotton Balls").Quantity)
        Assert.Equal(recordsBefore, countRecords())

        ' An item without a registered unit takes no stock changes until one is set.
        Using conn = Db.Open()
            Db.Execute(conn, Nothing, "UPDATE items SET unit = NULL WHERE id = @id", Db.P("@id", itemId))
        End Using
        Assert.True(Assert.Throws(Of InputValidationException)(Sub() StockService.StockIn(itemId, 2D, Date.Today, Nothing, Nothing, Nothing)).Errors.ContainsKey("unit"))
        Assert.True(Assert.Throws(Of InputValidationException)(Function() StockService.Adjust(itemId, AdjustmentMode.Add, 2D, "Count correction", Nothing, Nothing)).Errors.ContainsKey("unit"))
        Assert.Equal(1D, FindItem("Cotton Balls").Quantity)
        Assert.Equal(recordsBefore, countRecords())
    End Sub

    <IntegrationFact>
    Public Sub Initialize_StoresEachDropdownUnitUnderOneSpelling_AndKeepsCustomUnits()
        ResetDatabase()
        Dim supplierId = SupplierService.Add(New Supplier With {.Name = "Spelling Supplier", .ContactPerson = "Ana", .Phone = "09171234567", .Address = "Aparri"})
        Dim categoryId = LookupId(LookupKind.Category, "Grooming Supplies")
        ' Units typed as custom text by earlier versions, before these units were in the dropdown.
        Dim saved = {("Sachet Shampoo", "Sachet"), ("Lower Sachet", "sachet"), ("Spray Bottles", "bottles"), ("Brush", "Pieces"), ("Ear Drops", "ml"), ("Tray", "Tray")}
        Using conn = Db.Open()
            For Each row In saved
                Dim id = Db.Insert(conn, Nothing,
                    "INSERT INTO items (name, category_id, pet_type, quantity, unit, retail_price, supplier_id) VALUES (@name, @category, 'Both', 3, @unit, 10, @supplier)",
                    Db.P("@name", row.Item1), Db.P("@category", categoryId), Db.P("@unit", row.Item2), Db.P("@supplier", supplierId))
                Db.Execute(conn, Nothing,
                    "INSERT INTO stock_transactions (item_id, item_name, txn_type, qty_change, qty_before, qty_after, unit, txn_date) VALUES (@id, @name, 'INITIAL', 3, 0, 3, @unit, CURDATE())",
                    Db.P("@id", id), Db.P("@name", row.Item1), Db.P("@unit", row.Item2))
            Next
        End Using

        DatabaseInitializer.Initialize()

        Dim expected = {("Sachet Shampoo", "sachet"), ("Lower Sachet", "sachet"), ("Spray Bottles", "bottle"), ("Brush", "pcs"), ("Ear Drops", "ml"), ("Tray", "Tray")}
        For Each row In expected
            Assert.Equal(row.Item2, FindItem(row.Item1).Unit)
        Next
        Using conn = Db.Open()
            Dim units = Db.Query(conn, Nothing, "SELECT CONVERT(unit USING utf8mb4) COLLATE utf8mb4_bin AS unit FROM stock_transactions ORDER BY id", Function(r) Db.GetString(r, "unit"))
            Assert.Equal(expected.Select(Function(e) e.Item2), units)
        End Using
        ' The dropdown lists Sachet once, and a tidied item takes stock changes through the stock forms' Unit field.
        Assert.Single(InventoryService.GetUnitSuggestions(), Function(s) String.Equals(s, "Sachet", StringComparison.OrdinalIgnoreCase))
        Dim sachetId = FindItem("Sachet Shampoo").Id
        Assert.Null(StockService.CheckSelectedUnit(FindItem("Sachet Shampoo").Unit, "Sachet"))
        StockService.StockIn(sachetId, 2D, Date.Today, Nothing, Nothing, MeasurementUnits.Normalize("Sachet"))
        Assert.Equal("5 sachets", FindItem("Sachet Shampoo").QuantityText)
    End Sub
End Class
