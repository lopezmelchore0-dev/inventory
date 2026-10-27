Imports System.Text.Json
Imports MySqlConnector

''' <summary>Connection settings read from dbconfig.json next to the executable.</summary>
Public Class DbConfig
    Public Property Server As String = "localhost"
    Public Property Port As Integer = 3306
    Public Property User As String = "root"
    Public Property Password As String = ""
    Public Property Database As String = "pet_grooming_inventory"

    Public Shared ReadOnly Property FilePath As String
        Get
            Return IO.Path.Combine(AppContext.BaseDirectory, "dbconfig.json")
        End Get
    End Property

    Public Shared Function Load() As DbConfig
        Try
            If IO.File.Exists(FilePath) Then
                Dim options As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True}
                Dim cfg = JsonSerializer.Deserialize(Of DbConfig)(IO.File.ReadAllText(FilePath), options)
                If cfg IsNot Nothing Then Return cfg
            End If
        Catch ex As Exception
            ' A malformed file falls back to XAMPP defaults; the connection screen shows the effective settings.
        End Try
        Return New DbConfig()
    End Function

    Public Sub SaveIfMissing()
        If IO.File.Exists(FilePath) Then Return
        Dim options As New JsonSerializerOptions With {.WriteIndented = True}
        IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(Me, options))
    End Sub
End Class

''' <summary>Thin ADO.NET helper around MySqlConnector. All SQL in the app is parameterized.</summary>
Public NotInheritable Class Db
    Private Sub New()
    End Sub

    Public Shared Property Config As DbConfig = DbConfig.Load()

    Public Shared Function BuildConnectionString(includeDatabase As Boolean) As String
        Dim builder As New MySqlConnectionStringBuilder With {
            .Server = Config.Server,
            .Port = CUInt(Config.Port),
            .UserID = Config.User,
            .Password = Config.Password,
            .CharacterSet = "utf8mb4",
            .ConnectionTimeout = 5,
            .ConvertZeroDateTime = True
        }
        If includeDatabase Then builder.Database = Config.Database
        Return builder.ConnectionString
    End Function

    Public Shared Function Open() As MySqlConnection
        Dim conn As New MySqlConnection(BuildConnectionString(True))
        conn.Open()
        Return conn
    End Function

    Public Shared Function P(name As String, value As Object) As MySqlParameter
        Return New MySqlParameter(name, If(value, DBNull.Value))
    End Function

    Public Shared Function Execute(conn As MySqlConnection, tx As MySqlTransaction, sql As String, ParamArray params As MySqlParameter()) As Integer
        Using cmd = CreateCommand(conn, tx, sql, params)
            Return cmd.ExecuteNonQuery()
        End Using
    End Function

    ''' <summary>Runs an INSERT and returns the new AUTO_INCREMENT id.</summary>
    Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, sql As String, ParamArray params As MySqlParameter()) As Integer
        Using cmd = CreateCommand(conn, tx, sql, params)
            cmd.ExecuteNonQuery()
            Return CInt(cmd.LastInsertedId)
        End Using
    End Function

    Public Shared Function Scalar(conn As MySqlConnection, tx As MySqlTransaction, sql As String, ParamArray params As MySqlParameter()) As Object
        Using cmd = CreateCommand(conn, tx, sql, params)
            Dim result = cmd.ExecuteScalar()
            Return If(result Is DBNull.Value, Nothing, result)
        End Using
    End Function

    Public Shared Function Count(conn As MySqlConnection, tx As MySqlTransaction, sql As String, ParamArray params As MySqlParameter()) As Integer
        Return Convert.ToInt32(If(Scalar(conn, tx, sql, params), 0))
    End Function

    Public Shared Function Query(Of T)(conn As MySqlConnection, tx As MySqlTransaction, sql As String,
                                       map As Func(Of MySqlDataReader, T), ParamArray params As MySqlParameter()) As List(Of T)
        Dim results As New List(Of T)
        Using cmd = CreateCommand(conn, tx, sql, params)
            Using reader = cmd.ExecuteReader()
                While reader.Read()
                    results.Add(map(reader))
                End While
            End Using
        End Using
        Return results
    End Function

    Private Shared Function CreateCommand(conn As MySqlConnection, tx As MySqlTransaction, sql As String, params As MySqlParameter()) As MySqlCommand
        Dim cmd As New MySqlCommand(sql, conn, tx)
        If params IsNot Nothing AndAlso params.Length > 0 Then cmd.Parameters.AddRange(params)
        Return cmd
    End Function

    ' ---- Reader helpers ----

    Public Shared Function GetString(r As MySqlDataReader, column As String) As String
        Dim i = r.GetOrdinal(column)
        Return If(r.IsDBNull(i), Nothing, Convert.ToString(r.GetValue(i)))
    End Function

    Public Shared Function GetInt(r As MySqlDataReader, column As String) As Integer
        Return Convert.ToInt32(r.GetValue(r.GetOrdinal(column)))
    End Function

    Public Shared Function GetNullableInt(r As MySqlDataReader, column As String) As Integer?
        Dim i = r.GetOrdinal(column)
        If r.IsDBNull(i) Then Return Nothing
        Return Convert.ToInt32(r.GetValue(i))
    End Function

    Public Shared Function GetDecimal(r As MySqlDataReader, column As String) As Decimal
        Return Convert.ToDecimal(r.GetValue(r.GetOrdinal(column)))
    End Function

    Public Shared Function GetDate(r As MySqlDataReader, column As String) As Date
        Return Convert.ToDateTime(r.GetValue(r.GetOrdinal(column)))
    End Function

    Public Shared Function GetNullableDate(r As MySqlDataReader, column As String) As Date?
        Dim i = r.GetOrdinal(column)
        If r.IsDBNull(i) Then Return Nothing
        Return Convert.ToDateTime(r.GetValue(i)).Date
    End Function

    Public Shared Function GetBool(r As MySqlDataReader, column As String) As Boolean
        Return Convert.ToBoolean(r.GetValue(r.GetOrdinal(column)))
    End Function

    ''' <summary>Empty / whitespace strings are stored as NULL.</summary>
    Public Shared Function NullIfBlank(value As String) As Object
        If String.IsNullOrWhiteSpace(value) Then Return Nothing
        Return value.Trim()
    End Function
End Class
