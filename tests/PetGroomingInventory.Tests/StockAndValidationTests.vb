Imports Xunit

Public Class StockAndValidationTests

    <Fact>
    Public Sub ComputeNewQuantity_HandlesEveryMode()
        Assert.Equal(15D, StockService.ComputeNewQuantity(10D, AdjustmentMode.Add, 5D))
        Assert.Equal(7D, StockService.ComputeNewQuantity(10D, AdjustmentMode.Deduct, 3D))
        Assert.Equal(4D, StockService.ComputeNewQuantity(10D, AdjustmentMode.SetCount, 4D))
    End Sub

    <Fact>
    Public Sub Adjustment_CannotGoBelowZero()
        Dim errors = StockService.ValidateAdjustment(3D, AdjustmentMode.Deduct, 5D, "Damaged", Nothing)
        Assert.True(errors.ContainsKey("amount"))
    End Sub

    <Fact>
    Public Sub Adjustment_CanDeductToExactlyZero()
        Assert.Empty(StockService.ValidateAdjustment(3D, AdjustmentMode.Deduct, 3D, "Damaged", Nothing))
    End Sub

    <Fact>
    Public Sub Adjustment_RequiresReason_AndNoteForOther()
        Assert.True(StockService.ValidateAdjustment(10D, AdjustmentMode.Deduct, 1D, Nothing, Nothing).ContainsKey("reason"))
        Assert.True(StockService.ValidateAdjustment(10D, AdjustmentMode.Deduct, 1D, "Other", "").ContainsKey("note"))
        Assert.Empty(StockService.ValidateAdjustment(10D, AdjustmentMode.Deduct, 1D, "Other", "Given to a rescue shelter"))
    End Sub

    <Fact>
    Public Sub Adjustment_RejectsNoChangeAndNegativeAmounts()
        Assert.True(StockService.ValidateAdjustment(10D, AdjustmentMode.SetCount, 10D, "Count correction", Nothing).ContainsKey("amount"))
        Assert.True(StockService.ValidateAdjustment(10D, AdjustmentMode.Add, -2D, "Count correction", Nothing).ContainsKey("amount"))
        Assert.True(StockService.ValidateAdjustment(10D, AdjustmentMode.Add, 0D, "Count correction", Nothing).ContainsKey("amount"))
        Assert.Empty(StockService.ValidateAdjustment(10D, AdjustmentMode.SetCount, 0D, "Count correction", Nothing))
    End Sub

    <Fact>
    Public Sub ItemValidation_RequiresFields_AndRejectsNegatives()
        Dim errors = InventoryService.Validate(New Item With {.Name = "  ", .Quantity = -1D, .RetailPrice = -5D})
        For Each key In {"name", "category", "pettype", "supplier", "quantity", "unit", "price"}
            Assert.True(errors.ContainsKey(key), $"Expected an error for {key}")
        Next
    End Sub

    <Fact>
    Public Sub ItemValidation_ExpirationDateIsOptional()
        Dim item As New Item With {.Name = "Slicker Brush", .CategoryId = 1, .PetType = PetTypes.Both, .SupplierId = 1, .Quantity = 3D, .Unit = "pcs", .RetailPrice = 180D}
        Assert.Empty(InventoryService.Validate(item))
    End Sub

    <Theory>
    <InlineData("Dog", True)>
    <InlineData("Cat", True)>
    <InlineData("Both", True)>
    <InlineData("Bird", True)>
    <InlineData("Rabbit", True)>
    <InlineData("Guinea Pig", True)>
    <InlineData("1234567890123456789012345678901234567890", True)>    ' 40 characters
    <InlineData("", False)>
    <InlineData("   ", False)>
    <InlineData(Nothing, False)>
    <InlineData("12345678901234567890123456789012345678901", False)>   ' 41 characters
    Public Sub ItemValidation_PetTypeAcceptsCustomTypes(petType As String, valid As Boolean)
        Dim item As New Item With {.Name = "Slicker Brush", .CategoryId = 1, .PetType = petType, .SupplierId = 1, .Quantity = 3D, .Unit = "pcs", .RetailPrice = 180D}
        Assert.Equal(Not valid, InventoryService.Validate(item).ContainsKey("pettype"))
    End Sub

    <Fact>
    Public Sub ItemValidation_DescriptionIsOptionalButLimited()
        Dim item As New Item With {.Name = "Slicker Brush", .CategoryId = 1, .PetType = PetTypes.Dog, .SupplierId = 1, .Quantity = 3D, .Unit = "pcs", .RetailPrice = 180D,
                                   .Description = New String("x"c, InventoryService.MaxDescriptionLength + 1)}
        Assert.True(InventoryService.Validate(item).ContainsKey("description"))
        item.Description = "Gentle on sensitive skin"
        Assert.Empty(InventoryService.Validate(item))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("30 ml")>
    <InlineData("15 g Pink")>
    <InlineData("Stuffed Beef Cubes 500 g")>
    <InlineData("Anything the shop wants to write: 2 × 250 g / Lamb & Rice!")>
    Public Sub ItemValidation_DescriptionAcceptsAnyText(description As String)
        Dim item As New Item With {.Name = "Slicker Brush", .CategoryId = 1, .PetType = PetTypes.Dog, .SupplierId = 1, .Quantity = 3D, .Unit = "pcs", .RetailPrice = 180D,
                                   .Description = description}
        Assert.Empty(InventoryService.Validate(item))
    End Sub

    <Fact>
    Public Sub DescriptionSuggestions_DefaultsFirst_DuplicatesIgnoringCaseDropped()
        Dim merged = InventoryService.MergeSuggestions(DescriptionSuggestions.Defaults, {"30 ml", "lamb", "  ", "30 ML", "Chicken"})
        Assert.Equal(DescriptionSuggestions.Defaults.Concat({"30 ml", "Chicken"}), merged)
        For Each expected In {"ml", "L", "g", "kg", "tablets", "drops", "pieces", "Pink", "Green", "Blue", "Orange", "Strawberry",
                              "Milk", "Lamb", "BBQ", "Beef Flavor", "Stuffed Beef Cubes", "Sandwich Round Bar"}
            Assert.Contains(expected, merged)
        Next
    End Sub

    <Theory>
    <InlineData("", "Orange", True)>
    <InlineData("  ", "Orange", True)>
    <InlineData("ora", "Orange", True)>
    <InlineData("ORA", "Orange", True)>
    <InlineData("g", "Orange", True)>
    <InlineData("g", "Milk", False)>
    <InlineData("beef", "Stuffed Beef Cubes", True)>
    <InlineData("30 ml", "ml", False)>
    Public Sub SuggestionFilter_MatchesTypedTextAnywhere(typed As String, suggestion As String, expected As Boolean)
        Assert.Equal(expected, SuggestionBox.Matches(suggestion, typed))
    End Sub

    <Theory>
    <InlineData("", True)>
    <InlineData("   ", True)>
    <InlineData("sham", True)>                  ' part of a word
    <InlineData("OATMEAL", True)>               ' any case
    <InlineData("dog oat", True)>               ' every keyword, in any order
    <InlineData("  shampoo   dog ", True)>
    <InlineData("dog cat", False)>              ' one keyword missing
    <InlineData("500 ml", True)>                ' description (size)
    <InlineData("bath", True)>                  ' category
    <InlineData("conditioner", False)>
    Public Sub ItemSearch_MatchesEveryKeywordInNameDescriptionOrCategory(typed As String, expected As Boolean)
        Dim item As New Item With {.Name = "Oatmeal Dog Shampoo", .Description = "500 ml", .CategoryName = "Bath Supplies"}
        Assert.Equal(expected, ItemSearchBox.Matches(item, typed))
    End Sub

    <Fact>
    Public Sub ItemSearch_HandlesItemsWithoutDescription()
        Dim item As New Item With {.Name = "Slicker Brush", .Description = Nothing, .CategoryName = "Tools"}
        Assert.True(ItemSearchBox.Matches(item, "brush"))
        Assert.False(ItemSearchBox.Matches(item, "500"))
        Assert.Equal("Tools", item.PickerDetails)
    End Sub

    <Theory>
    <InlineData("Medicine", True)>
    <InlineData("Vitamins", True)>
    <InlineData("Pet Food & Treats", True)>
    <InlineData("Accessories", False)>
    <InlineData(Nothing, False)>
    Public Sub ExpiryTrackedCategories(category As String, tracked As Boolean)
        Assert.Equal(tracked, ItemCategories.IsExpiryTracked(category))
    End Sub

    <Theory>
    <InlineData("09171234567", True)>
    <InlineData("0917 123 4567", True)>
    <InlineData("+639171234567", True)>
    <InlineData("(078) 888-1234", True)>
    <InlineData("8881234", True)>
    <InlineData("12345", False)>
    <InlineData("call me", False)>
    Public Sub PhoneValidation(phone As String, expected As Boolean)
        Assert.Equal(expected, InputValidator.IsValidPhone(phone))
    End Sub

    <Theory>
    <InlineData("12", True, 12)>
    <InlineData("1,250.50", True, 1250.5)>
    <InlineData("abc", False, 0)>
    <InlineData("", False, 0)>
    Public Sub AmountParsing(text As String, ok As Boolean, expected As Double)
        Dim value As Decimal
        Assert.Equal(ok, InputValidator.TryParseAmount(text, value))
        If ok Then Assert.Equal(CDec(expected), value)
    End Sub
End Class
