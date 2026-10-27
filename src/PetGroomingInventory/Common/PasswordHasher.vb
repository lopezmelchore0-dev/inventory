Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>
''' Salted PBKDF2-HMAC-SHA256 hashing for passwords and security answers.
''' Stored format: PBKDF2-SHA256$iterations$saltBase64$hashBase64 (the iteration count can be raised later).
''' </summary>
Public NotInheritable Class PasswordHasher
    Private Sub New()
    End Sub

    Public Const Scheme As String = "PBKDF2-SHA256"
    Public Const DefaultIterations As Integer = 600000
    Private Const SaltBytes As Integer = 16
    Private Const HashBytes As Integer = 32
    Private Const MinIterations As Integer = 10000
    Private Const MaxIterations As Integer = 10000000

    Private Shared _iterations As Integer = DefaultIterations
    Private Shared ReadOnly _dummyHash As New Lazy(Of String)(Function() Hash("PawStock-dummy-" & Guid.NewGuid().ToString("N")))

    ''' <summary>Iterations for new hashes. Tests lower it to keep the suite fast; existing hashes keep their own count.</summary>
    Public Shared Property Iterations As Integer
        Get
            Return _iterations
        End Get
        Set(value As Integer)
            _iterations = Math.Clamp(value, MinIterations, MaxIterations)
        End Set
    End Property

    Public Shared Function Hash(secret As String) As String
        Dim salt = RandomNumberGenerator.GetBytes(SaltBytes)
        Dim derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(If(secret, "")), salt, _iterations, HashAlgorithmName.SHA256, HashBytes)
        Return $"{Scheme}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(derived)}"
    End Function

    ''' <summary>Constant-time check of a secret against a stored hash. Malformed hashes never verify.</summary>
    Public Shared Function Verify(secret As String, stored As String) As Boolean
        Dim iterations As Integer
        Dim salt As Byte() = Nothing
        Dim expected As Byte() = Nothing
        If Not TryParse(stored, iterations, salt, expected) Then Return False
        Dim actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(If(secret, "")), salt, iterations, HashAlgorithmName.SHA256, expected.Length)
        Return CryptographicOperations.FixedTimeEquals(actual, expected)
    End Function

    ''' <summary>
    ''' Runs a full verification against a throwaway hash. Called when there is no account to check,
    ''' so unknown usernames take as long as real ones.
    ''' </summary>
    Public Shared Sub VerifyDummy(secret As String)
        Verify(secret, _dummyHash.Value)
    End Sub

    Public Shared Function TryParse(stored As String, ByRef iterations As Integer, ByRef salt As Byte(), ByRef hashValue As Byte()) As Boolean
        If String.IsNullOrEmpty(stored) Then Return False
        Dim parts = stored.Split("$"c)
        If parts.Length <> 4 OrElse parts(0) <> Scheme Then Return False
        If Not Integer.TryParse(parts(1), iterations) OrElse iterations < MinIterations OrElse iterations > MaxIterations Then Return False
        Try
            salt = Convert.FromBase64String(parts(2))
            hashValue = Convert.FromBase64String(parts(3))
        Catch ex As FormatException
            Return False
        End Try
        Return salt.Length >= 8 AndAlso hashValue.Length >= 16
    End Function

    ''' <summary>Security answers are compared ignoring case and extra spaces ("  Aparri  " = "aparri").</summary>
    Public Shared Function NormalizeAnswer(answer As String) As String
        Return Regex.Replace(If(answer, "").Trim(), "\s+", " ").ToLowerInvariant()
    End Function
End Class
