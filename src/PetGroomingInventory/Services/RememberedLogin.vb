Imports System.Text.Json

''' <summary>
''' "Remember Me": keeps only the last username in %LOCALAPPDATA%\PawStock\login.json.
''' No password or session is ever stored, so the password is always required.
''' </summary>
Public NotInheritable Class RememberedLogin
    Private Sub New()
    End Sub

    Private Class Data
        Public Property Username As String
    End Class

    Private Shared _filePath As String

    ''' <summary>Overridable so tests never touch the real file.</summary>
    Public Shared Property FilePath As String
        Get
            Return If(_filePath, IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawStock", "login.json"))
        End Get
        Set(value As String)
            _filePath = value
        End Set
    End Property

    ''' <summary>The remembered username, or Nothing.</summary>
    Public Shared Function Load() As String
        Try
            If Not IO.File.Exists(FilePath) Then Return Nothing
            Dim value = JsonSerializer.Deserialize(Of Data)(IO.File.ReadAllText(FilePath))?.Username
            Return If(String.IsNullOrWhiteSpace(value) OrElse value.Length > PasswordPolicy.UsernameMaxLength, Nothing, value.Trim())
        Catch ex As Exception
            ' Unreadable file: behave as if nothing was remembered.
            Return Nothing
        End Try
    End Function

    Public Shared Sub Save(username As String)
        Try
            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(FilePath))
            IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(New Data With {.Username = username.Trim()}))
        Catch ex As Exception
            ' Remembering the username is a convenience; never block sign-in over it.
        End Try
    End Sub

    Public Shared Sub Clear()
        Try
            If IO.File.Exists(FilePath) Then IO.File.Delete(FilePath)
        Catch ex As Exception
            ' Same as Save: best effort.
        End Try
    End Sub
End Class
