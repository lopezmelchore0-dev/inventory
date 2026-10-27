''' <summary>
''' The single place where stock and expiration statuses are decided.
''' Pure functions so they can be unit-tested without a database.
''' </summary>
Public NotInheritable Class StatusCalculator
    Private Sub New()
    End Sub

    Public Const MaxThreshold As Integer = 100000
    Public Const MaxExpiringSoonDays As Integer = 365

    ''' <summary>
    ''' Out of Stock (0) → Critical (≤ critical) → Low (≤ low) → In Stock.
    ''' Thresholds are validated so that 0 &lt; critical &lt; low, which keeps the ranges from overlapping.
    ''' </summary>
    Public Shared Function GetStockStatus(quantity As Decimal, lowThreshold As Integer, criticalThreshold As Integer) As StockStatus
        If quantity <= 0D Then Return StockStatus.OutOfStock
        If quantity <= criticalThreshold Then Return StockStatus.CriticalStock
        If quantity <= lowThreshold Then Return StockStatus.LowStock
        Return StockStatus.InStock
    End Function

    ''' <summary>No date → None (never alerts). Past → Expired. Within N days (including today) → Expiring Soon.</summary>
    Public Shared Function GetExpirationStatus(expirationDate As Date?, today As Date, expiringSoonDays As Integer) As ExpirationStatus
        If Not expirationDate.HasValue Then Return ExpirationStatus.None
        Dim daysLeft = (expirationDate.Value.Date - today.Date).TotalDays
        If daysLeft < 0 Then Return ExpirationStatus.Expired
        If daysLeft <= expiringSoonDays Then Return ExpirationStatus.ExpiringSoon
        Return ExpirationStatus.Ok
    End Function

    ''' <summary>Returns field errors keyed "low", "critical", "days"; empty when the rules are valid.</summary>
    Public Shared Function ValidateRules(lowThreshold As Integer, criticalThreshold As Integer, expiringSoonDays As Integer) As Dictionary(Of String, String)
        Dim errors As New Dictionary(Of String, String)
        If criticalThreshold < 1 Then
            errors("critical") = "Critical stock threshold must be at least 1."
        ElseIf criticalThreshold >= MaxThreshold Then
            errors("critical") = $"Critical stock threshold must be below {MaxThreshold:N0}."
        End If
        If lowThreshold < 2 Then
            errors("low") = "Low stock threshold must be at least 2."
        ElseIf lowThreshold > MaxThreshold Then
            errors("low") = $"Low stock threshold cannot exceed {MaxThreshold:N0}."
        ElseIf Not errors.ContainsKey("critical") AndAlso lowThreshold <= criticalThreshold Then
            errors("low") = "Low stock threshold must be greater than the critical stock threshold."
        End If
        If expiringSoonDays < 1 OrElse expiringSoonDays > MaxExpiringSoonDays Then
            errors("days") = $"Expiring-soon period must be between 1 and {MaxExpiringSoonDays} days."
        End If
        Return errors
    End Function

    Public Shared Function StockLabel(status As StockStatus) As String
        Select Case status
            Case StockStatus.LowStock : Return "Low Stock"
            Case StockStatus.CriticalStock : Return "Critical Stock"
            Case StockStatus.OutOfStock : Return "Out of Stock"
            Case Else : Return "In Stock"
        End Select
    End Function

    Public Shared Function ExpirationLabel(status As ExpirationStatus) As String
        Select Case status
            Case ExpirationStatus.Expired : Return "Expired"
            Case ExpirationStatus.ExpiringSoon : Return "Expiring Soon"
            Case ExpirationStatus.Ok : Return "Not expiring soon"
            Case Else : Return "No expiration date"
        End Select
    End Function
End Class
