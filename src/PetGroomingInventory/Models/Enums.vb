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

''' <summary>Which animal an item is intended for. Stored as-is in items.pet_type.</summary>
Public NotInheritable Class PetTypes
    Private Sub New()
    End Sub

    Public Const Dog As String = "Dog"
    Public Const Cat As String = "Cat"
    Public Const Both As String = "Both"

    Public Shared ReadOnly All As String() = {Dog, Cat, Both}

    Public Shared Function IsValid(value As String) As Boolean
        Return All.Contains(value)
    End Function

    ''' <summary>Label shown in the inventory table, e.g. "Dog &amp; Cat" for Both.</summary>
    Public Shared Function Label(value As String) As String
        Return If(value = Both, "Dog & Cat", value)
    End Function

    ''' <summary>
    ''' Pet type filter shared by Inventory and Reports. Nothing matches everything; Dog / Cat also match
    ''' items meant for both, since those can be used on either animal; Both matches "Dog &amp; Cat" items only.
    ''' </summary>
    Public Shared Function Matches(itemPetType As String, filter As String) As Boolean
        If filter Is Nothing Then Return True
        Return itemPetType = filter OrElse (filter <> Both AndAlso itemPetType = Both)
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
