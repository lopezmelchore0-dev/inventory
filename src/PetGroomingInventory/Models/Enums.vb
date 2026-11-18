''' <summary>Stock level of an item. Ordered from least to most urgent.</summary>
Public Enum StockStatus
    InStock = 0
    LowStock = 1
    CriticalStock = 2
    OutOfStock = 3
End Enum

''' <summary>Expiration state of an item. None means no expiration date was entered.</summary>
Public Enum ExpirationStatus
    None = 0
    Ok = 1
    ExpiringSoon = 2
    Expired = 3
End Enum

Public Enum AdjustmentMode
    Add
    Deduct
    SetCount
End Enum

Public Enum LookupKind
    Category
    Unit
End Enum

Public NotInheritable Class ActivityTypes
    Private Sub New()
    End Sub

    Public Const ItemAdded As String = "Item added"
    Public Const ItemUpdated As String = "Item updated"
    Public Const ItemRemoved As String = "Item removed"
    Public Const StockReceived As String = "Stock received"
    Public Const StockAdjusted As String = "Stock adjusted"
    Public Const SupplierAdded As String = "Supplier added"
    Public Const SupplierUpdated As String = "Supplier updated"
    Public Const SupplierRemoved As String = "Supplier removed"
    Public Const CategoryAdded As String = "Category added"
    Public Const CategoryRemoved As String = "Category removed"
    Public Const UnitAdded As String = "Unit added"
    Public Const UnitRemoved As String = "Unit removed"
    Public Const SettingsUpdated As String = "Settings updated"
    Public Const BackupCreated As String = "Backup created"
    Public Const DataRestored As String = "Data restored"
    Public Const SampleDataLoaded As String = "Sample data loaded"
    Public Const SampleDataRemoved As String = "Sample data removed"
    Public Const AdminAccountCreated As String = "Admin account created"
    Public Const AdminProfileUpdated As String = "Admin profile updated"
    Public Const PasswordChanged As String = "Password changed"
    Public Const PasswordReset As String = "Password reset"
End Class

Public NotInheritable Class TxnTypes
    Private Sub New()
    End Sub

    Public Const Initial As String = "INITIAL"
    Public Const StockIn As String = "STOCK_IN"
    Public Const Adjustment As String = "ADJUSTMENT"

    Public Shared ReadOnly All As String() = {Initial, StockIn, Adjustment}
End Class

''' <summary>
''' Which animal an item is intended for, stored as-is in items.pet_type. Dog, Cat and Both are built in;
''' any other text the user types (e.g. "Rabbit") is kept as a custom pet type.
''' </summary>
Public NotInheritable Class PetTypes
    Private Sub New()
    End Sub

    Public Const Dog As String = "Dog"
    Public Const Cat As String = "Cat"
    Public Const Both As String = "Both"

    Public Const MaxLength As Integer = 40
    Public Const BothLabel As String = "Both (Dog & Cat)"
    Public Const OtherLabel As String = "Other (Custom Pet Type)"

    ''' <summary>The built-in pet types, in display order.</summary>
    Public Shared ReadOnly All As String() = {Dog, Cat, Both}

    ' Other ways of writing "Both" that are accepted in the editor.
    Private Shared ReadOnly BothAliases As String() = {Both, BothLabel, "Dog & Cat", "Dog and Cat", "Dogs & Cats", "Dogs and Cats"}

    ''' <summary>Any non-blank pet type up to MaxLength characters is allowed.</summary>
    Public Shared Function IsValid(value As String) As Boolean
        Dim trimmed = If(value, "").Trim()
        Return trimmed.Length > 0 AndAlso trimmed.Length <= MaxLength
    End Function

    Public Shared Function IsBuiltIn(value As String) As Boolean
        Return All.Contains(If(value, "").Trim(), StringComparer.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' The value to store for what is in the Pet Type box: Dog / Cat / Both (any case, or "Both (Dog &amp; Cat)") become the
    ''' built-in value, other text is kept as a custom pet type, and blank or "Other (Custom Pet Type)" give Nothing.
    ''' </summary>
    Public Shared Function Normalize(text As String) As String
        Dim trimmed = If(text, "").Trim()
        If trimmed.Length = 0 OrElse String.Equals(trimmed, OtherLabel, StringComparison.OrdinalIgnoreCase) Then Return Nothing
        If String.Equals(trimmed, Dog, StringComparison.OrdinalIgnoreCase) Then Return Dog
        If String.Equals(trimmed, Cat, StringComparison.OrdinalIgnoreCase) Then Return Cat
        If BothAliases.Contains(trimmed, StringComparer.OrdinalIgnoreCase) Then Return Both
        Return trimmed
    End Function

    ''' <summary>Label shown in the inventory table and reports, e.g. "Dog &amp; Cat" for Both. Custom types are shown as saved.</summary>
    Public Shared Function Label(value As String) As String
        Return If(value = Both, "Dog & Cat", value)
    End Function

    ''' <summary>Text shown in the editor's Pet Type box for a stored value.</summary>
    Public Shared Function EditorLabel(value As String) As String
        If String.IsNullOrWhiteSpace(value) Then Return ""
        Return If(value = Both, BothLabel, value)
    End Function

    ''' <summary>
    ''' Pet type filter shared by Inventory and Reports. Nothing matches everything; Dog / Cat also match
    ''' items meant for both, since those can be used on either animal; Both and custom types match their own items only.
    ''' </summary>
    Public Shared Function Matches(itemPetType As String, filter As String) As Boolean
        If filter Is Nothing Then Return True
        If String.Equals(itemPetType, filter, StringComparison.OrdinalIgnoreCase) Then Return True
        Return (filter = Dog OrElse filter = Cat) AndAlso itemPetType = Both
    End Function

    ''' <summary>The built-in types first, then the distinct custom types among the values (ignoring case), alphabetically.</summary>
    Public Shared Function Ordered(values As IEnumerable(Of String)) As List(Of String)
        Dim seen As New HashSet(Of String)(All, StringComparer.OrdinalIgnoreCase)
        Dim custom As New List(Of String)
        For Each v In values
            Dim trimmed = If(v, "").Trim()
            If trimmed.Length > 0 AndAlso seen.Add(trimmed) Then custom.Add(trimmed)
        Next
        Return All.Concat(custom.OrderBy(Function(c) c, StringComparer.CurrentCultureIgnoreCase)).ToList()
    End Function

    ''' <summary>Pet type filter entries: "All pet types", then Dog, Cat, Dog &amp; Cat and the custom pet types among the values.</summary>
    Public Shared Function FilterOptions(petTypesInUse As IEnumerable(Of String)) As List(Of FilterOption)
        Dim options As New List(Of FilterOption) From {New FilterOption("All pet types", Nothing)}
        options.AddRange(Ordered(petTypesInUse).Select(Function(p) New FilterOption(Label(p), p)))
        Return options
    End Function
End Class

''' <summary>
''' Unit of measure an item is stocked in (items.unit). Predefined units are stored as their short value
''' (e.g. "pcs", "bottle"); anything else the user types is kept as a custom unit. Quantities are never converted between units.
''' The same catalogue drives the Unit field of Add / Edit Item, Stock In and Stock Adjustment (see UnitField).
''' </summary>
Public NotInheritable Class MeasurementUnits
    Private Sub New()
    End Sub

    Public Const MaxLength As Integer = 40
    ''' <summary>Last dropdown entry: a prompt to type a custom unit, never stored.</summary>
    Public Const OtherLabel As String = "Others"
    ' The prompt's label in earlier versions, still recognised as "no unit chosen yet".
    Private Const LegacyOtherLabel As String = "Other (Custom Unit)"

    ''' <summary>
    ''' Dropdown label, stored value and plural of each predefined unit, in dropdown order. Whole = counted in whole
    ''' numbers only (a gallon can be partly used, so it takes decimals).
    ''' </summary>
    Public Shared ReadOnly Predefined As (Label As String, Value As String, Plural As String, Whole As Boolean)() = {
        ("Pieces", "pcs", "pcs", True),
        ("Bottle", "bottle", "bottles", True),
        ("Gallon", "gallon", "gallons", False),
        ("Pack", "pack", "packs", True),
        ("Box", "box", "boxes", True),
        ("Set", "set", "sets", True),
        ("Bundle", "bundle", "bundles", True),
        ("Bag", "bag", "bags", True),
        ("Sachet", "sachet", "sachets", True),
        ("Can", "can", "cans", True),
        ("Roll", "roll", "rolls", True),
        ("Pair", "pair", "pairs", True),
        ("Tub", "tub", "tubs", True),
        ("Pail", "pail", "pails", True),
        ("Jar", "jar", "jars", True),
        ("Tube", "tube", "tubes", True)
    }

    ' Other ways of typing a predefined unit, so the same unit is never saved twice under different spellings.
    Private Shared ReadOnly Aliases As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"pc", "pcs"}, {"piece", "pcs"}, {"Pieces (pcs)", "pcs"}, {"gal", "gallon"}, {"Per Box", "box"}, {"Per Bundle", "bundle"}
    }

    ' Units offered by earlier versions that are no longer in the dropdown. Items that still use them keep their
    ' plural and whole-number rule; they are shown like custom units.
    Private Shared ReadOnly LegacyPlurals As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"batch", "batches"}, {"tablet", "tablets"}, {"drop", "drops"}
    }

    ''' <summary>Dropdown entries: the predefined labels, then "Others".</summary>
    Public Shared ReadOnly Property Labels As IEnumerable(Of String)
        Get
            Return Predefined.Select(Function(u) u.Label).Concat({OtherLabel})
        End Get
    End Property

    ''' <summary>
    ''' The value to store for what is in the Unit box: a predefined label, value or plural (any case) becomes its value,
    ''' other text is kept as a custom unit, and blank or "Others" give Nothing (no unit chosen yet).
    ''' </summary>
    Public Shared Function Normalize(text As String) As String
        Dim trimmed = If(text, "").Trim()
        If trimmed.Length = 0 OrElse String.Equals(trimmed, OtherLabel, StringComparison.OrdinalIgnoreCase) OrElse
           String.Equals(trimmed, LegacyOtherLabel, StringComparison.OrdinalIgnoreCase) Then Return Nothing
        For Each u In Predefined
            If String.Equals(trimmed, u.Label, StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(trimmed, u.Value, StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(trimmed, u.Plural, StringComparison.OrdinalIgnoreCase) Then Return u.Value
        Next
        Dim aliased As String = Nothing
        If Aliases.TryGetValue(trimmed, aliased) Then Return aliased
        Return trimmed
    End Function

    ''' <summary>True when two stored or typed units are the same unit (custom units ignore case).</summary>
    Public Shared Function SameUnit(a As String, b As String) As Boolean
        Return String.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Text shown in the Unit box for a stored unit: the dropdown label, or the custom text as saved.</summary>
    Public Shared Function LabelFor(value As String) As String
        If String.IsNullOrWhiteSpace(value) Then Return ""
        Dim normalized = Normalize(value)
        For Each u In Predefined
            If u.Value = normalized Then Return u.Label
        Next
        Return value.Trim()
    End Function

    ''' <summary>Counted units (pieces, bottles, boxes…) take whole numbers only.</summary>
    Public Shared Function IsCountable(value As String) As Boolean
        If value Is Nothing Then Return False
        For Each u In Predefined
            If u.Value = value Then Return u.Whole
        Next
        Return LegacyPlurals.ContainsKey(value)
    End Function

    ''' <summary>The unit as shown after a quantity: "box" for 1, "boxes" otherwise. Custom units are shown as typed.</summary>
    Public Shared Function Display(value As String, quantity As Decimal) As String
        If String.IsNullOrWhiteSpace(value) Then Return ""
        If Math.Abs(quantity) <> 1D Then
            For Each u In Predefined
                If u.Value = value Then Return u.Plural
            Next
            Dim plural As String = Nothing
            If LegacyPlurals.TryGetValue(value, plural) Then Return plural
        End If
        Return value.Trim()
    End Function

    ''' <summary>True for the stored value of a dropdown unit (e.g. "pcs", "bottle"), exactly as stored.</summary>
    Public Shared Function IsPredefined(value As String) As Boolean
        Return Predefined.Any(Function(u) u.Value = value)
    End Function

    ''' <summary>
    ''' The stored value a unit saved by an earlier version should have, when it is another spelling of a dropdown unit
    ''' (e.g. "Sachet" or "bottles" typed as a custom unit); otherwise Nothing (custom units are kept as they are).
    ''' </summary>
    Public Shared Function CanonicalFor(saved As String) As String
        Dim normalized = Normalize(saved)
        If normalized Is Nothing OrElse Not IsPredefined(normalized) OrElse String.Equals(saved, normalized, StringComparison.Ordinal) Then Return Nothing
        Return normalized
    End Function
End Class

''' <summary>Starter suggestions for the item Description box. Examples only: any text can be typed instead.</summary>
Public NotInheritable Class DescriptionSuggestions
    Private Sub New()
    End Sub

    Public Shared ReadOnly Defaults As String() = {
        "ml", "L", "g", "kg", "tablets", "drops", "pieces",
        "Pink", "Green", "Blue",
        "Orange", "Strawberry", "Milk", "Lamb", "BBQ", "Beef Flavor", "Stuffed Beef Cubes", "Sandwich Round Bar"
    }
End Class

''' <summary>Category names the app treats specially. Categories themselves are user-editable lookups.</summary>
Public NotInheritable Class ItemCategories
    Private Sub New()
    End Sub

    Public Const Medicine As String = "Medicine"
    Public Const Vitamins As String = "Vitamins"
    Public Const PetFoodAndTreats As String = "Pet Food & Treats"
    Public Const LegacyMedicineAndVitamins As String = "Medicine & Vitamins"

    Private Shared ReadOnly ExpiryTracked As String() = {Medicine, Vitamins, PetFoodAndTreats}

    ''' <summary>Categories whose products normally carry a printed expiration date.</summary>
    Public Shared Function IsExpiryTracked(categoryName As String) As Boolean
        Return ExpiryTracked.Contains(If(categoryName, "").Trim(), StringComparer.OrdinalIgnoreCase)
    End Function
End Class
