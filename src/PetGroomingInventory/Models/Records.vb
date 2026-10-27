''' <summary>A category or a unit of measure.</summary>
Public Class LookupItem
    Public Property Id As Integer
    Public Property Name As String = ""
    Public Property UsageCount As Integer

    Public ReadOnly Property UsageText As String
        Get
            Return If(UsageCount = 0, "Not used", $"Used by {Fmt.Plural(UsageCount, "item")}")
        End Get
    End Property
End Class

Public Class StockTransaction
    Public Property Id As Integer
    Public Property ItemId As Integer?
    Public Property ItemName As String = ""
    Public Property TxnType As String = ""
    Public Property QtyChange As Decimal
    Public Property QtyBefore As Decimal
    Public Property QtyAfter As Decimal
    Public Property SupplierId As Integer?
    Public Property SupplierName As String
    Public Property TxnDate As Date
    Public Property Reason As String
    Public Property Note As String
    Public Property CreatedAt As Date

    Public ReadOnly Property TypeLabel As String
        Get
            Select Case TxnType
                Case TxnTypes.Initial : Return "Opening stock"
                Case TxnTypes.StockIn : Return "Stock in"
                Case Else : Return "Adjustment"
            End Select
        End Get
    End Property

    Public ReadOnly Property TxnDateText As String
        Get
            Return Fmt.DateText(TxnDate)
        End Get
    End Property

    Public ReadOnly Property CreatedAtText As String
        Get
            Return Fmt.DateTimeText(CreatedAt)
        End Get
    End Property

    Public ReadOnly Property ChangeText As String
        Get
            Return Fmt.SignedQty(QtyChange)
        End Get
    End Property

    Public ReadOnly Property BeforeAfterText As String
        Get
            Return $"{Fmt.Qty(QtyBefore)} → {Fmt.Qty(QtyAfter)}"
        End Get
    End Property

    Public ReadOnly Property SupplierText As String
        Get
            Return If(String.IsNullOrWhiteSpace(SupplierName), "—", SupplierName)
        End Get
    End Property

    Public ReadOnly Property NoteText As String
        Get
            Return If(String.IsNullOrWhiteSpace(Note), "—", Note)
        End Get
    End Property

    Public ReadOnly Property ReasonText As String
        Get
            Return If(String.IsNullOrWhiteSpace(Reason), "—", Reason)
        End Get
    End Property
End Class

Public Class ActivityEntry
    Public Property Id As Integer
    Public Property SubjectName As String = ""
    Public Property ActivityType As String = ""
    Public Property Description As String = ""
    Public Property PerformedBy As String
    Public Property CreatedAt As Date

    Public ReadOnly Property CreatedAtText As String
        Get
            Return Fmt.DateTimeText(CreatedAt)
        End Get
    End Property
End Class

Public Class AppSettings
    Public Const DefaultLowThreshold As Integer = 10
    Public Const DefaultCriticalThreshold As Integer = 5
    Public Const DefaultExpiringSoonDays As Integer = 30
    Public Const DefaultAppName As String = "PawStock"

    Public Property LowStockThreshold As Integer = DefaultLowThreshold
    Public Property CriticalStockThreshold As Integer = DefaultCriticalThreshold
    Public Property ExpiringSoonDays As Integer = DefaultExpiringSoonDays
    Public Property AppName As String = DefaultAppName
End Class

Public Class DashboardSummary
    Public Property TotalItems As Integer
    Public Property AvailableStock As Decimal
    Public Property InStock As Integer
    Public Property LowStock As Integer
    Public Property CriticalStock As Integer
    Public Property OutOfStock As Integer
    Public Property ExpiringSoon As Integer
    Public Property Expired As Integer

    Public ReadOnly Property NeedsRestock As Integer
        Get
            Return LowStock + CriticalStock + OutOfStock
        End Get
    End Property

    Public ReadOnly Property TotalAlerts As Integer
        Get
            Return NeedsRestock + ExpiringSoon + Expired
        End Get
    End Property
End Class

''' <summary>Items plus the settings used to compute their statuses, loaded together.</summary>
Public Class InventorySnapshot
    Public Property Items As List(Of Item)
    Public Property Settings As AppSettings
    Public Property Summary As DashboardSummary
End Class

''' <summary>An entry in a filter drop-down. Value Nothing means "all".</summary>
Public Class FilterOption
    Public Property Label As String
    Public Property Value As Object

    Public Sub New(label As String, value As Object)
        Me.Label = label
        Me.Value = value
    End Sub
End Class
