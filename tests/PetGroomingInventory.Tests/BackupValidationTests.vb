Imports Xunit

Public Class BackupValidationTests

    Private Shared Function ValidBackup() As BackupFile
        Dim created = New Date(2026, 10, 24, 9, 0, 0)
        Dim data As New BackupData With {
            .Categories = New List(Of LookupRow) From {New LookupRow With {.Id = 1, .Name = "Shampoo & Conditioner"}},
            .Units = New List(Of LookupRow) From {New LookupRow With {.Id = 1, .Name = "bottle"}},
            .Suppliers = New List(Of SupplierRow) From {
                New SupplierRow With {.Id = 1, .Name = "Test Supplier", .ContactPerson = "Juan", .Phone = "09171234567",
                                      .Address = "Aparri, Cagayan", .CreatedAt = created, .UpdatedAt = created}
            },
            .Items = New List(Of ItemRow) From {
                New ItemRow With {.Id = 1, .Name = "Oatmeal Shampoo", .CategoryId = 1, .UnitId = 1, .SupplierId = 1,
                                  .Quantity = 12.0D, .RetailPrice = 285.0D, .ExpirationDate = New Date(2027, 1, 1),
                                  .CreatedAt = created, .UpdatedAt = created}
            },
            .StockTransactions = New List(Of StockTransactionRow) From {
                New StockTransactionRow With {.Id = 1, .ItemId = 1, .ItemName = "Oatmeal Shampoo", .TxnType = TxnTypes.Initial,
                                              .QtyChange = 12D, .QtyBefore = 0D, .QtyAfter = 12D, .TxnDate = created.Date, .CreatedAt = created}
            },
            .Activities = New List(Of ActivityRow) From {
                New ActivityRow With {.Id = 1, .SubjectName = "Oatmeal Shampoo", .ActivityType = ActivityTypes.ItemAdded,
                                      .Description = "Added", .CreatedAt = created}
            },
            .Settings = New List(Of SettingRow) From {
                New SettingRow With {.Key = "low_stock_threshold", .Value = "10"},
                New SettingRow With {.Key = "critical_stock_threshold", .Value = "5"},
                New SettingRow With {.Key = "expiring_soon_days", .Value = "30"}
            }
        }
        Return New BackupFile With {
            .AppId = BackupService.AppId,
            .FormatVersion = BackupService.FormatVersion,
            .CreatedAt = created,
            .Data = data,
            .Checksum = BackupService.ComputeChecksum(data)
        }
    End Function

    <Fact>
    Public Sub ValidBackup_PassesValidation()
        Assert.Empty(BackupService.Validate(ValidBackup()))
    End Sub

    <Fact>
    Public Sub TamperedData_FailsChecksum()
        Dim file = ValidBackup()
        file.Data.Items(0).Quantity = 999D
        Assert.Contains(BackupService.Validate(file), Function(e) e.Contains("checksum"))
    End Sub

    <Fact>
    Public Sub WrongApp_IsRejected()
        Dim file = ValidBackup()
        file.AppId = "SomethingElse"
        Assert.NotEmpty(BackupService.Validate(file))
    End Sub

    <Fact>
    Public Sub MissingSection_IsRejected()
        Dim file = ValidBackup()
        file.Data.Suppliers = Nothing
        Assert.NotEmpty(BackupService.Validate(file))
    End Sub

    <Fact>
    Public Sub NegativeQuantity_IsRejected()
        Dim file = ValidBackup()
        file.Data.Items(0).Quantity = -1D
        file.Checksum = BackupService.ComputeChecksum(file.Data)
        Assert.Contains(BackupService.Validate(file), Function(e) e.Contains("invalid quantity"))
    End Sub

    <Fact>
    Public Sub BrokenSupplierReference_IsRejected()
        Dim file = ValidBackup()
        file.Data.Items(0).SupplierId = 42
        file.Checksum = BackupService.ComputeChecksum(file.Data)
        Assert.Contains(BackupService.Validate(file), Function(e) e.Contains("missing supplier"))
    End Sub

    <Fact>
    Public Sub InvalidPetType_IsRejected()
        Dim file = ValidBackup()
        file.Data.Items(0).PetType = "Bird"
        file.Checksum = BackupService.ComputeChecksum(file.Data)
        Assert.Contains(BackupService.Validate(file), Function(e) e.Contains("invalid pet type"))
    End Sub

    <Fact>
    Public Sub BackupFromBeforePetTypes_StillPassesChecksum()
        ' Older backups have no PetType/Description keys; their checksum was computed without them.
        Dim file = ValidBackup()
        Dim json = System.Text.Json.JsonSerializer.Serialize(file)
        Assert.DoesNotContain("PetType", json)
        Dim restored = System.Text.Json.JsonSerializer.Deserialize(Of BackupFile)(json)
        Assert.Null(restored.Data.Items(0).PetType)
        Assert.Empty(BackupService.Validate(restored))
    End Sub

    <Fact>
    Public Sub PetTypeAndDescription_SurviveJsonRoundTrip()
        Dim file = ValidBackup()
        file.Data.Items(0).PetType = PetTypes.Dog
        file.Data.Items(0).Description = "For sensitive skin"
        file.Checksum = BackupService.ComputeChecksum(file.Data)
        Dim restored = System.Text.Json.JsonSerializer.Deserialize(Of BackupFile)(System.Text.Json.JsonSerializer.Serialize(file))
        Assert.Equal(PetTypes.Dog, restored.Data.Items(0).PetType)
        Assert.Equal("For sensitive skin", restored.Data.Items(0).Description)
        Assert.Empty(BackupService.Validate(restored))
    End Sub

    <Fact>
    Public Sub ItemWithoutUnit_IsValid_ButMissingUnitReferenceIsRejected()
        Dim file = ValidBackup()
        file.Data.Items(0).UnitId = Nothing
        file.Data.Items(0).Description = "500 ml"
        file.Checksum = BackupService.ComputeChecksum(file.Data)
        Dim restored = System.Text.Json.JsonSerializer.Deserialize(Of BackupFile)(System.Text.Json.JsonSerializer.Serialize(file))
        Assert.Null(restored.Data.Items(0).UnitId)
        Assert.Empty(BackupService.Validate(restored))

        file.Data.Items(0).UnitId = 99
        file.Checksum = BackupService.ComputeChecksum(file.Data)
        Assert.Contains(BackupService.Validate(file), Function(e) e.Contains("missing unit"))
    End Sub

    <Fact>
    Public Sub ChecksumSurvivesJsonRoundTrip()
        Dim file = ValidBackup()
        Dim json = System.Text.Json.JsonSerializer.Serialize(file)
        Dim restored = System.Text.Json.JsonSerializer.Deserialize(Of BackupFile)(json)
        Assert.Empty(BackupService.Validate(restored))
    End Sub
End Class
