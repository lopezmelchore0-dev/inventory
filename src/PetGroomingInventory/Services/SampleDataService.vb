Imports MySqlConnector

''' <summary>
''' Optional demonstration records. Everything created here is flagged is_sample = 1, shown with a
''' "Sample" tag, and can be removed in one step. Nothing is loaded automatically.
''' </summary>
Public NotInheritable Class SampleDataService
    Private Sub New()
    End Sub

    Private Class SampleItem
        Public Name As String
        Public Category As String
        Public PetType As String = PetTypes.Both
        Public Description As String
        Public Unit As String = "pcs"
        Public Price As Decimal
        Public Supplier As Integer
        Public Quantity As Func(Of AppSettings, Decimal)
        Public ExpiresInDays As Integer?
    End Class

    Public Shared Function CountSampleItems() As Integer
        Using conn = Db.Open()
            Return Db.Count(conn, Nothing, "SELECT COUNT(*) FROM items WHERE is_sample = 1") +
                   Db.Count(conn, Nothing, "SELECT COUNT(*) FROM suppliers WHERE is_sample = 1")
        End Using
    End Function

    Public Shared Function Load() As String
        Dim suppliers As New List(Of Supplier) From {
            New Supplier With {.Name = "Cagayan Pet Supplies Trading", .ContactPerson = "Sample Contact A", .Phone = "09170000001",
                               .Address = "Rizal St., Centro, Aparri, Cagayan", .Email = "orders@sample-cagayanpet.ph"},
            New Supplier With {.Name = "Tuguegarao Vet & Grooming Depot", .ContactPerson = "Sample Contact B", .Phone = "(078) 000 0002",
                               .Address = "Bonifacio St., Tuguegarao City, Cagayan"},
            New Supplier With {.Name = "North Luzon Pet Essentials", .ContactPerson = "Sample Contact C", .Phone = "09180000003",
                               .Address = "National Highway, Gonzaga, Cagayan", .Email = "sales@sample-nlpet.ph"}
        }

        ' Quantities are derived from the current thresholds so every stock status is represented.
        Dim samples As New List(Of SampleItem) From {
            New SampleItem With {.Name = "Oatmeal Dog Shampoo 500ml", .Category = "Shampoo & Conditioner", .PetType = PetTypes.Dog, .Description = "500 ml", .Price = 285D, .Supplier = 0,
                                 .Quantity = Function(s) s.LowStockThreshold + 14, .ExpiresInDays = 240},
            New SampleItem With {.Name = "Anti-Tick & Flea Shampoo 250ml", .Category = "Shampoo & Conditioner", .PetType = PetTypes.Dog, .Description = "250 ml", .Price = 320D, .Supplier = 1,
                                 .Quantity = Function(s) s.LowStockThreshold, .ExpiresInDays = 20},
            New SampleItem With {.Name = "Puppy Detangling Conditioner", .Category = "Shampoo & Conditioner", .PetType = PetTypes.Dog, .Description = "250 ml", .Price = 260D, .Supplier = 0,
                                 .Quantity = Function(s) s.CriticalStockThreshold, .ExpiresInDays = 400},
            New SampleItem With {.Name = "Slicker Brush (Medium)", .Category = "Grooming Tools", .Description = "Medium", .Price = 180D, .Supplier = 2,
                                 .Quantity = Function(s) s.LowStockThreshold + 2, .ExpiresInDays = Nothing},
            New SampleItem With {.Name = "Nail Clipper (Large Breed)", .Category = "Grooming Tools", .PetType = PetTypes.Dog, .Description = "Large breed", .Price = 350D, .Supplier = 2,
                                 .Quantity = Function(s) 0D, .ExpiresInDays = Nothing},
            New SampleItem With {.Name = "Ear Cleaning Solution 120ml", .Category = ItemCategories.Medicine, .Description = "120 ml", .Price = 210D, .Supplier = 1,
                                 .Quantity = Function(s) s.LowStockThreshold + 5, .ExpiresInDays = -15},
            New SampleItem With {.Name = "Cat Multivitamin Paste 100g", .Category = ItemCategories.Vitamins, .PetType = PetTypes.Cat, .Description = "100 g", .Price = 340D, .Supplier = 1,
                                 .Quantity = Function(s) s.LowStockThreshold + 3, .ExpiresInDays = 180},
            New SampleItem With {.Name = "Pet Cologne Baby Powder 250ml", .Category = "Grooming Supplies", .Description = "250 ml", .Price = 175D, .Supplier = 0,
                                 .Quantity = Function(s) s.LowStockThreshold + 8, .ExpiresInDays = 520},
            New SampleItem With {.Name = "Cotton Balls 100pcs", .Category = "Grooming Supplies", .Description = "100 pieces", .Unit = "pack", .Price = 45D, .Supplier = 2,
                                 .Quantity = Function(s) s.LowStockThreshold + 30, .ExpiresInDays = Nothing},
            New SampleItem With {.Name = "Pet Wet Wipes 80 sheets", .Category = "Grooming Supplies", .Description = "80 sheets", .Unit = "pack", .Price = 95D, .Supplier = 0,
                                 .Quantity = Function(s) s.LowStockThreshold + 6, .ExpiresInDays = 25},
            New SampleItem With {.Name = "Chicken Jerky Dog Treats", .Category = "Pet Food & Treats", .PetType = PetTypes.Dog, .Description = "Chicken", .Unit = "box", .Price = 120D, .Supplier = 1,
                                 .Quantity = Function(s) s.LowStockThreshold + 20, .ExpiresInDays = 75},
            New SampleItem With {.Name = "Kennel Disinfectant Spray 1L", .Category = "Cleaning Supplies", .Description = "1 L", .Price = 195D, .Supplier = 2,
                                 .Quantity = Function(s) Math.Max(1, s.CriticalStockThreshold - 1), .ExpiresInDays = 300},
            New SampleItem With {.Name = "Pet Bandana (Assorted)", .Category = "Accessories", .Description = "Assorted colors", .Price = 60D, .Supplier = 0,
                                 .Quantity = Function(s) s.LowStockThreshold + 40, .ExpiresInDays = Nothing}
        }

        Dim itemsAdded = 0
        Dim suppliersAdded = 0
        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                If Db.Count(conn, tx, "SELECT COUNT(*) FROM items WHERE is_sample = 1") > 0 Then
                    Throw New BusinessRuleException("Sample data is already loaded. Remove it first if you want to load it again.")
                End If
                Dim settings = SettingsRepository.Load(conn, tx)

                Dim supplierIds As New List(Of Integer)
                For Each s In suppliers
                    Dim existingId = SupplierRepository.FindIdByName(conn, tx, s.Name)
                    If existingId.HasValue Then
                        supplierIds.Add(existingId.Value)
                    Else
                        s.IsSample = True
                        supplierIds.Add(SupplierRepository.Insert(conn, tx, s))
                        suppliersAdded += 1
                    End If
                Next

                For Each sample In samples
                    If ItemRepository.NameExists(conn, tx, sample.Name, 0) Then Continue For
                    Dim item As New Item With {
                        .Name = sample.Name,
                        .CategoryId = EnsureLookup(conn, tx, LookupKind.Category, sample.Category),
                        .PetType = sample.PetType,
                        .Description = sample.Description,
                        .Quantity = sample.Quantity(settings),
                        .Unit = sample.Unit,
                        .RetailPrice = sample.Price,
                        .SupplierId = supplierIds(sample.Supplier),
                        .ExpirationDate = If(sample.ExpiresInDays.HasValue, Date.Today.AddDays(sample.ExpiresInDays.Value), CType(Nothing, Date?)),
                        .IsSample = True
                    }
                    Dim id = ItemRepository.Insert(conn, tx, item)
                    If item.Quantity > 0 Then
                        StockRepository.Insert(conn, tx, New StockTransaction With {
                            .ItemId = id, .ItemName = item.Name, .TxnType = TxnTypes.Initial,
                            .QtyChange = item.Quantity, .QtyBefore = 0D, .QtyAfter = item.Quantity, .Unit = item.Unit,
                            .SupplierId = item.SupplierId, .SupplierName = suppliers(sample.Supplier).Name,
                            .TxnDate = Date.Today, .Note = "Sample data opening stock"
                        })
                    End If
                    itemsAdded += 1
                Next

                ActivityRepository.Log(conn, tx, "Sample data", ActivityTypes.SampleDataLoaded,
                    $"Loaded {Fmt.Plural(itemsAdded, "sample item")} and {Fmt.Plural(suppliersAdded, "sample supplier")}")
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
        Return $"Loaded {Fmt.Plural(itemsAdded, "sample item")} and {Fmt.Plural(suppliersAdded, "sample supplier")}."
    End Function

    Public Shared Function Remove() As String
        Dim itemsRemoved As Integer
        Dim suppliersRemoved As Integer
        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                itemsRemoved = Db.Execute(conn, tx, "DELETE FROM items WHERE is_sample = 1")
                ' Sample suppliers that real items now use are kept (and become regular suppliers).
                Db.Execute(conn, tx,
                    "UPDATE suppliers s SET s.is_sample = 0 WHERE s.is_sample = 1 AND EXISTS (SELECT 1 FROM items i WHERE i.supplier_id = s.id)")
                suppliersRemoved = Db.Execute(conn, tx, "DELETE FROM suppliers WHERE is_sample = 1")
                If itemsRemoved = 0 AndAlso suppliersRemoved = 0 Then
                    Throw New BusinessRuleException("There is no sample data to remove.")
                End If
                ActivityRepository.Log(conn, tx, "Sample data", ActivityTypes.SampleDataRemoved,
                    $"Removed {Fmt.Plural(itemsRemoved, "sample item")} and {Fmt.Plural(suppliersRemoved, "sample supplier")}")
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
        Return $"Removed {Fmt.Plural(itemsRemoved, "sample item")} and {Fmt.Plural(suppliersRemoved, "sample supplier")}."
    End Function

    Private Shared Function EnsureLookup(conn As MySqlConnection, tx As MySqlTransaction, kind As LookupKind, name As String) As Integer
        Dim id = LookupRepository.FindIdByName(conn, tx, kind, name)
        If id.HasValue Then Return id.Value
        Return LookupRepository.Insert(conn, tx, kind, name)
    End Function
End Class
