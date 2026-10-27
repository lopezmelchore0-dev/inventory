Imports System.Security.Cryptography
Imports System.Text

''' <summary>A signed-in Admin as the UI sees it. Password and answer hashes never leave the data layer.</summary>
Public Class AdminAccount
    Public Const AdminRole As String = "Admin"

    Public Property Id As Integer
    Public Property FullName As String = ""
    Public Property Username As String = ""
    Public Property SecurityQuestion As String = ""
    Public Property Role As String = AdminRole
    Public Property LastLoginAt As Date?
    Public Property PasswordChangedAt As Date
    Public Property CreatedAt As Date

    Public ReadOnly Property Initials As String
        Get
            Dim parts = FullName.Split(" "c, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length = 0 Then Return "A"
            If parts.Length = 1 Then Return parts(0).Substring(0, 1).ToUpperInvariant()
            Return (parts(0).Substring(0, 1) & parts(parts.Length - 1).Substring(0, 1)).ToUpperInvariant()
        End Get
    End Property
End Class

''' <summary>
''' The fixed list of security questions. A fixed list (no custom questions) lets password recovery show
''' an equally plausible stand-in question for usernames that do not exist.
''' </summary>
Public NotInheritable Class SecurityQuestions
    Private Sub New()
    End Sub

    Public Shared ReadOnly All As IReadOnlyList(Of String) = New List(Of String) From {
        "What was the name of your first pet?",
        "What is your mother's maiden name?",
        "In what town or city were you born?",
        "What was the name of your elementary school?",
        "What is the name of your favorite teacher?",
        "What was your childhood nickname?",
        "What is the name of the street you grew up on?",
        "What was the make of your first car or motorcycle?"
    }

    ''' <summary>Deterministic stand-in question for a username, so asking twice gives the same answer.</summary>
    Public Shared Function DecoyFor(username As String, secretKey As String) As String
        Dim key = Encoding.UTF8.GetBytes(If(secretKey, ""))
        Dim data = Encoding.UTF8.GetBytes(If(username, "").Trim().ToLowerInvariant())
        Dim digest = HMACSHA256.HashData(key, data)
        Dim index = CInt(BitConverter.ToUInt32(digest, 0) Mod CUInt(All.Count))
        Return All(index)
    End Function
End Class
