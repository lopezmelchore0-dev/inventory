Imports System.Globalization

''' <summary>Maps a status to its theme brush. Parameter: "Bg" (badge fill), "Fg" (badge text) or "Dot" (solid color).</summary>
Public NotInheritable Class StatusPalette
    Private Sub New()
    End Sub

    Public Shared Function StockBrush(status As StockStatus, part As String) As Brush
        Dim prefix As String
        Select Case status
            Case StockStatus.LowStock : prefix = "LowStock"
            Case StockStatus.CriticalStock : prefix = "CriticalStock"
            Case StockStatus.OutOfStock : prefix = "OutOfStock"
            Case Else : prefix = "InStock"
        End Select
        Dim suffix = If(part = "Bg", "BgBrush", If(part = "Fg", "FgBrush", "Brush"))
        Return Find(prefix & suffix)
    End Function

    Public Shared Function ExpiryBrush(status As ExpirationStatus, part As String) As Brush
        Select Case status
            Case ExpirationStatus.Expired
                Return If(part = "Bg", Find("PrimaryDarkBrush"), Brushes.White)
            Case ExpirationStatus.ExpiringSoon
                Return If(part = "Bg", Find("LavenderBrush"), Find("PrimaryDarkBrush"))
            Case Else
                Return If(part = "Bg", Brushes.Transparent, Find("TextSecondaryBrush"))
        End Select
    End Function

    Public Shared Function Find(key As String) As Brush
        Return If(TryCast(Application.Current?.TryFindResource(key), Brush), Brushes.Gray)
    End Function
End Class

Public Class StockStatusBrushConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        If TypeOf value IsNot StockStatus Then Return Binding.DoNothing
        Return StatusPalette.StockBrush(DirectCast(value, StockStatus), If(TryCast(parameter, String), "Fg"))
    End Function

    Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        Throw New NotSupportedException()
    End Function
End Class

Public Class ExpiryBrushConverter
    Implements IValueConverter

    Public Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        If TypeOf value IsNot ExpirationStatus Then Return Binding.DoNothing
        Return StatusPalette.ExpiryBrush(DirectCast(value, ExpirationStatus), If(TryCast(parameter, String), "Fg"))
    End Function

    Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        Throw New NotSupportedException()
    End Function
End Class
