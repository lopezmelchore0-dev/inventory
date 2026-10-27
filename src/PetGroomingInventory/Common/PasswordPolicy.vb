Imports System.Text.RegularExpressions

''' <summary>One password requirement, shown as a live checklist under password fields.</summary>
Public Class PasswordRule
    Public Property Key As String
    Public Property Text As String
    Public Property IsMet As Boolean
End Class

''' <summary>Password strength requirements and account field rules shared by registration, reset and change password.</summary>
Public NotInheritable Class PasswordPolicy
    Private Sub New()
    End Sub

    Public Const MinLength As Integer = 8
    Public Const MaxLength As Integer = 128
    Public Const UsernameMinLength As Integer = 4
    Public Const UsernameMaxLength As Integer = 30
    Public Const FullNameMinLength As Integer = 2
    Public Const FullNameMaxLength As Integer = 100
    Public Const AnswerMinLength As Integer = 2
    Public Const AnswerMaxLength As Integer = 100

    Private Shared ReadOnly UsernamePattern As New Regex("^[A-Za-z0-9._]+$", RegexOptions.Compiled)

    ''' <summary>Every requirement with whether the password meets it. Username is optional (used for the "doesn't contain your username" rule).</summary>
    Public Shared Function Evaluate(password As String, Optional username As String = Nothing) As List(Of PasswordRule)
        Dim pwd = If(password, "")
        Dim user = If(username, "").Trim()
        Return New List(Of PasswordRule) From {
            New PasswordRule With {.Key = "length", .Text = $"{MinLength} to {MaxLength} characters", .IsMet = pwd.Length >= MinLength AndAlso pwd.Length <= MaxLength},
            New PasswordRule With {.Key = "upper", .Text = "An uppercase letter (A–Z)", .IsMet = pwd.Any(AddressOf Char.IsUpper)},
            New PasswordRule With {.Key = "lower", .Text = "A lowercase letter (a–z)", .IsMet = pwd.Any(AddressOf Char.IsLower)},
            New PasswordRule With {.Key = "digit", .Text = "A number (0–9)", .IsMet = pwd.Any(AddressOf Char.IsDigit)},
            New PasswordRule With {.Key = "symbol", .Text = "A symbol, e.g. ! @ # $ %", .IsMet = pwd.Any(Function(c) Not Char.IsLetterOrDigit(c) AndAlso Not Char.IsWhiteSpace(c))},
            New PasswordRule With {.Key = "username", .Text = "Does not contain your username",
                                   .IsMet = pwd.Length > 0 AndAlso (user.Length < UsernameMinLength OrElse pwd.IndexOf(user, StringComparison.OrdinalIgnoreCase) < 0)}
        }
    End Function

    ''' <summary>Returns a message describing what the password is missing, or Nothing when it meets every rule.</summary>
    Public Shared Function Check(password As String, Optional username As String = Nothing) As String
        If String.IsNullOrEmpty(password) Then Return "Password is required."
        Dim unmet = Evaluate(password, username).Where(Function(r) Not r.IsMet).ToList()
        If unmet.Count = 0 Then Return Nothing
        If unmet.Any(Function(r) r.Key = "username") AndAlso unmet.Count = 1 Then Return "Password must not contain your username."
        Return "Password needs: " & String.Join(", ", unmet.Select(Function(r) r.Text.ToLowerInvariant())) & "."
    End Function

    ''' <summary>0 = empty, 1 = weak, 2 = fair, 3 = good, 4 = strong.</summary>
    Public Shared Function Score(password As String, Optional username As String = Nothing) As Integer
        If String.IsNullOrEmpty(password) Then Return 0
        Dim met = Evaluate(password, username).Where(Function(r) r.IsMet).Count()
        Dim allMet = met = 6
        If Not allMet Then Return If(met >= 4, 2, 1)
        Return If(password.Length >= 12, 4, 3)
    End Function

    Public Shared Function ScoreLabel(score As Integer) As String
        Select Case score
            Case 1 : Return "Weak"
            Case 2 : Return "Fair"
            Case 3 : Return "Good"
            Case 4 : Return "Strong"
            Case Else : Return ""
        End Select
    End Function

    Public Shared Function CheckUsername(username As String) As String
        Dim text = If(username, "").Trim()
        If text.Length = 0 Then Return "Username is required."
        If text.Length < UsernameMinLength OrElse text.Length > UsernameMaxLength Then
            Return $"Username must be {UsernameMinLength} to {UsernameMaxLength} characters."
        End If
        If Not UsernamePattern.IsMatch(text) Then Return "Username may only contain letters, numbers, dots (.) and underscores (_)."
        Return Nothing
    End Function

    Public Shared Function CheckFullName(fullName As String) As String
        Dim text = If(fullName, "").Trim()
        If text.Length = 0 Then Return "Full name is required."
        If text.Length < FullNameMinLength OrElse text.Length > FullNameMaxLength Then
            Return $"Full name must be {FullNameMinLength} to {FullNameMaxLength} characters."
        End If
        Return Nothing
    End Function

    Public Shared Function CheckQuestion(question As String) As String
        If String.IsNullOrWhiteSpace(question) Then Return "Choose a security question."
        If Not SecurityQuestions.All.Contains(question) Then Return "Choose a security question from the list."
        Return Nothing
    End Function

    Public Shared Function CheckAnswer(answer As String) As String
        Dim text = PasswordHasher.NormalizeAnswer(answer)
        If text.Length = 0 Then Return "Security answer is required."
        If text.Length < AnswerMinLength OrElse text.Length > AnswerMaxLength Then
            Return $"Security answer must be {AnswerMinLength} to {AnswerMaxLength} characters."
        End If
        Return Nothing
    End Function
End Class
