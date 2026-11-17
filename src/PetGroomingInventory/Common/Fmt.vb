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

    ''' <summary>"12 pcs", "1 box". Items without a unit (added before units existed) show the number only.</summary>
    Public Shared Function Qty(value As Decimal, unit As String) As String
        Return WithUnit(Qty(value), value, unit)
    End Function

    Public Shared Function SignedQty(value As Decimal, unit As String) As String
        Return WithUnit(SignedQty(value), value, unit)
    End Function

    Private Shared Function WithUnit(number As String, value As Decimal, unit As String) As String
        Dim word = MeasurementUnits.Display(unit, value)
        Return If(word.Length = 0, number, number & " " & word)
    End Function

    ''' <summary>
    ''' Total per unit, since quantities in different units cannot be added: "120 pcs · 3,500 ml · 4 (no unit)".
    ''' A single unit gives a single total, e.g. "15 pcs" (or "15" when no item has a unit).
    ''' </summary>
    Public Shared Function QtyByUnit(entries As IEnumerable(Of (Qty As Decimal, Unit As String)), Optional signed As Boolean = False) As String
        Dim groups = entries.GroupBy(Function(e) If(e.Unit, "").Trim(), StringComparer.Ordinal).
                             OrderBy(Function(g) g.Key.Length = 0).ThenBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase).ToList()
        If groups.Count = 0 Then Return If(signed, SignedQty(0D), Qty(0D))
        Dim parts = groups.Select(
            Function(g)
                Dim total = g.Sum(Function(e) e.Qty)
                If g.Key.Length = 0 Then
                    Dim number = If(signed, SignedQty(total), Qty(total))
                    Return If(groups.Count = 1, number, number & " (no unit)")
                End If
                Return If(signed, SignedQty(total, g.Key), Qty(total, g.Key))
            End Function)
        Return String.Join(" · ", parts)
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
