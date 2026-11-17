Imports Xunit

''' <summary>Pet type rules: built-in Dog / Cat / Both plus any custom pet type the user types.</summary>
Public Class PetTypeTests

    <Theory>
    <InlineData("Dog", "Dog")>
    <InlineData("dog", "Dog")>
    <InlineData(" CAT ", "Cat")>
    <InlineData("Both", "Both")>
    <InlineData("both", "Both")>
    <InlineData("Both (Dog & Cat)", "Both")>
    <InlineData("Dog & Cat", "Both")>
    <InlineData("dog and cat", "Both")>
    <InlineData("Rabbit", "Rabbit")>
    <InlineData("  Guinea Pig  ", "Guinea Pig")>
    <InlineData("Other (Custom Pet Type)", Nothing)>
    <InlineData("", Nothing)>
    <InlineData("   ", Nothing)>
    <InlineData(Nothing, Nothing)>
    Public Sub Normalize_KeepsBuiltInValuesAndCustomText(text As String, expected As String)
        Assert.Equal(expected, PetTypes.Normalize(text))
    End Sub

    <Fact>
    Public Sub Labels_ShowBothAsDogAndCat_AndCustomTypesAsSaved()
        Assert.Equal("Dog & Cat", PetTypes.Label(PetTypes.Both))
        Assert.Equal("Dog", PetTypes.Label(PetTypes.Dog))
        Assert.Equal("Rabbit", PetTypes.Label("Rabbit"))
        Assert.Equal("Both (Dog & Cat)", PetTypes.EditorLabel(PetTypes.Both))
        Assert.Equal("Rabbit", PetTypes.EditorLabel("Rabbit"))
        Assert.Equal("", PetTypes.EditorLabel(Nothing))
        ' What the editor shows is read back as the same value.
        For Each value In {PetTypes.Dog, PetTypes.Cat, PetTypes.Both, "Rabbit"}
            Assert.Equal(value, PetTypes.Normalize(PetTypes.EditorLabel(value)))
        Next
    End Sub

    <Theory>
    <InlineData("Dog", Nothing, True)>
    <InlineData("Rabbit", Nothing, True)>
    <InlineData("Dog", "Dog", True)>
    <InlineData("Both", "Dog", True)>      ' Dog & Cat items can be used on dogs
    <InlineData("Both", "Cat", True)>
    <InlineData("Cat", "Dog", False)>
    <InlineData("Rabbit", "Dog", False)>   ' custom types are not dog or cat items
    <InlineData("Rabbit", "Both", False)>
    <InlineData("Dog", "Both", False)>
    <InlineData("Both", "Both", True)>
    <InlineData("Rabbit", "Rabbit", True)>
    <InlineData("rabbit", "Rabbit", True)>
    <InlineData("Both", "Rabbit", False)>
    Public Sub Matches_DogAndCatIncludeBoth_CustomTypesMatchOnlyThemselves(itemType As String, filter As String, expected As Boolean)
        Assert.Equal(expected, PetTypes.Matches(itemType, filter))
    End Sub

    <Fact>
    Public Sub Ordered_ListsBuiltInsFirst_ThenEachCustomTypeOnce()
        Dim ordered = PetTypes.Ordered({"Rabbit", "Dog", "bird", "rabbit", "", Nothing, "both", "Bird"})
        Assert.Equal({"Dog", "Cat", "Both", "bird", "Rabbit"}, ordered)
    End Sub

    <Fact>
    Public Sub FilterOptions_StartWithAll_AndIncludeCustomTypes()
        Dim options = PetTypes.FilterOptions({"Hamster"})
        Assert.Equal({"All pet types", "Dog", "Cat", "Dog & Cat", "Hamster"}, options.Select(Function(o) o.Label))
        Assert.Null(options(0).Value)
        Assert.Equal(PetTypes.Both, options(3).Value)
        Assert.DoesNotContain(options, Function(o) o.Label.Contains("only"))
    End Sub

    <Fact>
    Public Sub IsValid_AllowsAnyPetTypeUpToTheLimit()
        Assert.True(PetTypes.IsValid("Bird"))
        Assert.True(PetTypes.IsValid(New String("x"c, PetTypes.MaxLength)))
        Assert.False(PetTypes.IsValid(New String("x"c, PetTypes.MaxLength + 1)))
        Assert.False(PetTypes.IsValid(" "))
        Assert.False(PetTypes.IsValid(Nothing))
    End Sub
End Class
