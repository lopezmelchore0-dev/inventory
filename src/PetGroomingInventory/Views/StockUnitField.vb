''' <summary>
''' The read-only Unit field shared by Stock In and Stock Adjustment. It always shows the selected item's registered
''' unit (pluralized for the typed quantity, e.g. "5 bundles"), so stock changes are never entered in another unit.
''' </summary>
Public NotInheritable Class StockUnitField
    Private Sub New()
    End Sub

    Public Const NoUnitText As String = "No unit set"

    Public Shared Sub Refresh(unitBox As TextBox, unitError As TextBlock, item As Item, quantityText As String)
        If item Is Nothing Then
            unitBox.Text = ""
            unitBox.ToolTip = Nothing
            SetError(unitError, Nothing)
            Return
        End If

        Dim problem = StockService.CheckUnit(item.Unit, item.Unit)
        If problem IsNot Nothing Then
            unitBox.Text = NoUnitText
            unitBox.ToolTip = Nothing
            SetError(unitError, problem)
            Return
        End If

        ' Pluralize for the typed amount: "1 bundle", "5 bundles"; nothing typed yet shows the plural form.
        Dim qty As Decimal
        If Not InputValidator.TryParseAmount(quantityText, qty) Then qty = 2D
        unitBox.Text = MeasurementUnits.Display(item.Unit, qty)
        unitBox.ToolTip = $"Unit registered for this item: {MeasurementUnits.LabelFor(item.Unit)}"
        SetError(unitError, Nothing)
    End Sub

    Private Shared Sub SetError(label As TextBlock, message As String)
        label.Text = If(message, "")
        label.Visibility = If(message Is Nothing, Visibility.Collapsed, Visibility.Visible)
    End Sub
End Class
