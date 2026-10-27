Imports System.Globalization
Imports MySqlConnector

Public NotInheritable Class SettingsRepository
    Private Sub New()
    End Sub

    Public Const LowStockKey As String = "low_stock_threshold"
    Public Const CriticalStockKey As String = "critical_stock_threshold"
    Public Const ExpiringSoonKey As String = "expiring_soon_days"
    Public Const AppNameKey As String = "app_name"

    Public Shared Function Load(conn As MySqlConnection, Optional tx As MySqlTransaction = Nothing) As AppSettings
        Dim values = Db.Query(Of KeyValuePair(Of String, String))(conn, tx,
            "SELECT setting_key, setting_value FROM settings",
            Function(r) New KeyValuePair(Of String, String)(Db.GetString(r, "setting_key"), Db.GetString(r, "setting_value"))).
            ToDictionary(Function(kv) kv.Key, Function(kv) kv.Value)

        Dim settings As New AppSettings With {
            .LowStockThreshold = ReadInt(values, LowStockKey, AppSettings.DefaultLowThreshold),
            .CriticalStockThreshold = ReadInt(values, CriticalStockKey, AppSettings.DefaultCriticalThreshold),
            .ExpiringSoonDays = ReadInt(values, ExpiringSoonKey, AppSettings.DefaultExpiringSoonDays)
        }
        Dim name As String = Nothing
        If values.TryGetValue(AppNameKey, name) AndAlso Not String.IsNullOrWhiteSpace(name) Then settings.AppName = name

        ' Guard against hand-edited rows that break the non-overlap rule.
        If StatusCalculator.ValidateRules(settings.LowStockThreshold, settings.CriticalStockThreshold, settings.ExpiringSoonDays).Count > 0 Then
            settings.LowStockThreshold = AppSettings.DefaultLowThreshold
            settings.CriticalStockThreshold = AppSettings.DefaultCriticalThreshold
            settings.ExpiringSoonDays = AppSettings.DefaultExpiringSoonDays
        End If
        Return settings
    End Function

    Public Shared Sub Save(conn As MySqlConnection, tx As MySqlTransaction, key As String, value As String)
        Db.Execute(conn, tx,
            "INSERT INTO settings (setting_key, setting_value) VALUES (@key, @value) " &
            "ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)",
            Db.P("@key", key), Db.P("@value", value))
    End Sub

    Private Shared Function ReadInt(values As Dictionary(Of String, String), key As String, fallback As Integer) As Integer
        Dim raw As String = Nothing
        Dim parsed As Integer
        If values.TryGetValue(key, raw) AndAlso Integer.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, parsed) Then
            Return parsed
        End If
        Return fallback
    End Function
End Class
