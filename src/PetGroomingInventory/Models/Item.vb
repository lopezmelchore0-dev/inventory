Public Class Item
    Public Property Id As Integer
    Public Property Name As String = ""
    Public Property CategoryId As Integer
    Public Property CategoryName As String = ""
    Public Property PetType As String = ""
    Public Property Quantity As Decimal
    Public Property RetailPrice As Decimal
    Public Property SupplierId As Integer
    Public Property SupplierName As String = ""
    Public Property ExpirationDate As Date?
    Public Property Description As String
    Public Property IsSample As Boolean
    ''' <summary>When the item was added (items.created_at).</summary>
    Public Property CreatedAt As Date

    ' Computed from the current settings when items are loaded (see InventoryService.ApplyStatuses).
    Public Property Status As StockStatus
    Public Property ExpiryStatus As ExpirationStatus
    Public Property DaysToExpiration As Integer?

    Public ReadOnly Property QuantityText As String
        Get
            Return Fmt.Qty(Quantity)
        End Get
    End Property

    ''' <summary>Quantity × retail price. The system stores no cost price, so this is the only inventory value available.</summary>
    Public ReadOnly Property RetailValue As Decimal
        Get
            Return Quantity * RetailPrice
        End Get
    End Property

    Public ReadOnly Property PriceText As String
        Get
            Return Fmt.Money(RetailPrice)
        End Get
    End Property

    Public ReadOnly Property ExpirationText As String
        Get
            Return Fmt.DateText(ExpirationDate)
        End Get
    End Property

    Public ReadOnly Property PetTypeText As String
        Get
            Return PetTypes.Label(PetType)
        End Get
    End Property

    Public ReadOnly Property HasDescription As Boolean
        Get
            Return Not String.IsNullOrWhiteSpace(Description)
        End Get
    End Property

    Public ReadOnly Property StatusText As String
        Get
            Return StatusCalculator.StockLabel(Status)
        End Get
    End Property

    Public ReadOnly Property ExpirationLabel As String
        Get
            Return StatusCalculator.ExpirationLabel(ExpiryStatus)
        End Get
    End Property

    Public ReadOnly Property HasExpirationAlert As Boolean
        Get
            Return ExpiryStatus = ExpirationStatus.Expired OrElse ExpiryStatus = ExpirationStatus.ExpiringSoon
        End Get
    End Property

    Public ReadOnly Property NeedsRestock As Boolean
        Get
            Return Status <> StockStatus.InStock
        End Get
    End Property

    ''' <summary>Human-friendly time left, e.g. "12 days left" or "Expired 3 days ago".</summary>
    Public ReadOnly Property ExpirationDetail As String
        Get
            If Not DaysToExpiration.HasValue Then Return "No expiration date"
            Dim days = DaysToExpiration.Value
            If days < 0 Then Return $"Expired {Fmt.Plural(-days, "day")} ago"
            If days = 0 Then Return "Expires today"
            Return $"{Fmt.Plural(days, "day")} left"
        End Get
    End Property

    ''' <summary>Label used in item pickers.</summary>
    Public ReadOnly Property DisplayName As String
        Get
            Return $"{Name}  ({QuantityText})"
        End Get
    End Property
End Class
