Imports Xunit

Public Class StatusCalculatorTests

    ' Defaults: low = 10, critical = 5
    <Theory>
    <InlineData(0, StockStatus.OutOfStock)>
    <InlineData(0.5, StockStatus.CriticalStock)>
    <InlineData(1, StockStatus.CriticalStock)>
    <InlineData(5, StockStatus.CriticalStock)>
    <InlineData(5.01, StockStatus.LowStock)>
    <InlineData(6, StockStatus.LowStock)>
    <InlineData(10, StockStatus.LowStock)>
    <InlineData(10.5, StockStatus.InStock)>
    <InlineData(11, StockStatus.InStock)>
    <InlineData(500, StockStatus.InStock)>
    Public Sub StockStatus_UsesNonOverlappingRanges(quantity As Double, expected As StockStatus)
        Assert.Equal(expected, StatusCalculator.GetStockStatus(CDec(quantity), 10, 5))
    End Sub

    <Fact>
    Public Sub OutOfStock_IsSeparateFromCritical()
        Assert.Equal(StockStatus.OutOfStock, StatusCalculator.GetStockStatus(0D, 10, 5))
        Assert.NotEqual(StockStatus.OutOfStock, StatusCalculator.GetStockStatus(0.01D, 10, 5))
    End Sub

    <Fact>
    Public Sub Rules_RejectOverlappingThresholds()
        Assert.True(StatusCalculator.ValidateRules(5, 5, 30).ContainsKey("low"))
        Assert.True(StatusCalculator.ValidateRules(4, 5, 30).ContainsKey("low"))
        Assert.True(StatusCalculator.ValidateRules(10, 0, 30).ContainsKey("critical"))
        Assert.True(StatusCalculator.ValidateRules(10, 5, 0).ContainsKey("days"))
        Assert.True(StatusCalculator.ValidateRules(10, 5, 400).ContainsKey("days"))
        Assert.Empty(StatusCalculator.ValidateRules(10, 5, 30))
        Assert.Empty(StatusCalculator.ValidateRules(2, 1, 1))
    End Sub

    <Fact>
    Public Sub Expiration_NoDate_NeverAlerts()
        Assert.Equal(ExpirationStatus.None, StatusCalculator.GetExpirationStatus(Nothing, New Date(2026, 10, 24), 30))
    End Sub

    <Fact>
    Public Sub Expiration_ClassifiesByDaysLeft()
        Dim today = New Date(2026, 10, 24)
        Assert.Equal(ExpirationStatus.Expired, StatusCalculator.GetExpirationStatus(today.AddDays(-1), today, 30))
        Assert.Equal(ExpirationStatus.ExpiringSoon, StatusCalculator.GetExpirationStatus(today, today, 30))
        Assert.Equal(ExpirationStatus.ExpiringSoon, StatusCalculator.GetExpirationStatus(today.AddDays(30), today, 30))
        Assert.Equal(ExpirationStatus.Ok, StatusCalculator.GetExpirationStatus(today.AddDays(31), today, 30))
    End Sub

    <Fact>
    Public Sub Expiration_IgnoresTimeOfDay()
        Dim today = New Date(2026, 10, 24, 23, 59, 0)
        Assert.Equal(ExpirationStatus.ExpiringSoon, StatusCalculator.GetExpirationStatus(New Date(2026, 10, 24), today, 30))
    End Sub
End Class
