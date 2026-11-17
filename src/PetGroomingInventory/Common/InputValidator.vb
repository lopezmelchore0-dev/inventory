Imports System.Globalization
Imports System.Text.RegularExpressions

Public NotInheritable Class InputValidator
    Private Sub New()
    End Sub

    ''' <summary>Largest quantity or price accepted anywhere in the app.</summary>
    Public Const MaxAmount As Decimal = 1000000D

    Private Shared ReadOnly EmailPattern As New Regex("^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.Compiled)
    ' PH mobile (09XXXXXXXXX / +639XXXXXXXXX), landline with area code (0XX XXX XXXX), or a 7–8 digit local number.
    Private Shared ReadOnly PhonePattern As New Regex("^(\+63\d{9,10}|0\d{9,10}|\d{7,8})$", RegexOptions.Compiled)

    ''' <summary>Parses "1,250.5" style input. Returns False for blank or non-numeric text.</summary>
    Public Shared Function TryParseAmount(text As String, ByRef value As Decimal) As Boolean
        value = 0D
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim cleaned = text.Trim().Replace(",", "").Replace(Fmt.Peso, "")
        Return Decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign Or NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, value)
    End Function

    Public Shared Function TryParseWholeNumber(text As String, ByRef value As Integer) As Boolean
        value = 0
        If String.IsNullOrWhiteSpace(text) Then Return False
        Return Integer.TryParse(text.Trim().Replace(",", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, value)
    End Function

    Public Shared Function HasAtMostTwoDecimals(value As Decimal) As Boolean
        Return Decimal.Round(value, 2) = value
    End Function

    Public Shared Function IsValidEmail(email As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(email) AndAlso EmailPattern.IsMatch(email.Trim())
    End Function

    Public Shared Function IsValidPhone(phone As String) As Boolean
        If String.IsNullOrWhiteSpace(phone) Then Return False
        Dim digits = Regex.Replace(phone.Trim(), "[\s\-\(\)\.]", "")
        Return PhonePattern.IsMatch(digits)
    End Function

    ''' <summary>Validates a non-negative amount (quantity or price) and returns an error message, or Nothing.</summary>
    Public Shared Function CheckAmount(value As Decimal, label As String, allowZero As Boolean) As String
        If value < 0D Then Return $"{label} cannot be negative."
        If Not allowZero AndAlso value = 0D Then Return $"{label} must be greater than zero."
        If value > MaxAmount Then Return $"{label} cannot exceed {MaxAmount:N0}."
        If Not HasAtMostTwoDecimals(value) Then Return $"{label} can have at most 2 decimal places."
        Return Nothing
    End Function

    ''' <summary>Counted units (pcs, box, tablets…) take whole numbers only. Returns an error message, or Nothing.</summary>
    Public Shared Function CheckWholeForUnit(value As Decimal, unit As String, label As String) As String
        If MeasurementUnits.IsCountable(unit) AndAlso Decimal.Truncate(value) <> value Then
            Return $"{label} must be a whole number for {MeasurementUnits.Display(unit, 2D)} (e.g. 3, not 2.5)."
        End If
        Return Nothing
    End Function
End Class
