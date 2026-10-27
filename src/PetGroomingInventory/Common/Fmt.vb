Imports System.Globalization

''' <summary>Consistent display formatting for quantities, money and dates.</summary>
Public NotInheritable Class Fmt
    Private Sub New()
    End Sub

    Public Shared ReadOnly Peso As String = ChrW(&H20B1)
    Private Shared ReadOnly Invariant As CultureInfo = CultureInfo.InvariantCulture

    Public Shared Function Qty(value As Decimal) As String
        Return value.ToString("#,##0.##", Invariant)
    End Function

    Public Shared Function SignedQty(value As Decimal) As String
        If value > 0 Then Return "+" & Qty(value)
        If value < 0 Then Return "−" & Qty(-value)
        Return "0"
    End Function

    Public Shared Function Money(value As Decimal) As String
        Return Peso & value.ToString("#,##0.00", Invariant)
    End Function

    Public Shared Function DateText(value As Date?) As String
        If Not value.HasValue Then Return "—"
        Return value.Value.ToString("MMM d, yyyy", Invariant)
    End Function

    Public Shared Function DateTimeText(value As Date) As String
        Return value.ToString("MMM d, yyyy  h:mm tt", Invariant)
    End Function

    ''' <summary>"1 item", "3 items".</summary>
    Public Shared Function Plural(count As Integer, singular As String, Optional pluralForm As String = Nothing) As String
        Dim word = If(count = 1, singular, If(pluralForm, singular & "s"))
        Return $"{count.ToString("#,##0", Invariant)} {word}"
    End Function

    Public Shared Function Truncate(text As String, maxLength As Integer) As String
        If text Is Nothing OrElse text.Length <= maxLength Then Return text
        Return text.Substring(0, maxLength - 1) & "…"
    End Function
End Class
