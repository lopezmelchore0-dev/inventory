Imports System.Globalization
Imports MySqlConnector

Public NotInheritable Class SettingsService
    Private Sub New()
    End Sub

    Public Shared Function GetSettings() As AppSettings
        Using conn = Db.Open()
            Return SettingsRepository.Load(conn)
        End Using
    End Function

    Public Shared Sub SaveStockRules(lowThreshold As Integer, criticalThreshold As Integer, expiringSoonDays As Integer)
        Dim errors = StatusCalculator.ValidateRules(lowThreshold, criticalThreshold, expiringSoonDays)
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                Dim current = SettingsRepository.Load(conn, tx)
                SettingsRepository.Save(conn, tx, SettingsRepository.LowStockKey, lowThreshold.ToString(CultureInfo.InvariantCulture))
                SettingsRepository.Save(conn, tx, SettingsRepository.CriticalStockKey, criticalThreshold.ToString(CultureInfo.InvariantCulture))
                SettingsRepository.Save(conn, tx, SettingsRepository.ExpiringSoonKey, expiringSoonDays.ToString(CultureInfo.InvariantCulture))
                ActivityRepository.Log(conn, tx, "Stock status rules", ActivityTypes.SettingsUpdated,
                    $"Low stock at or below {lowThreshold} (was {current.LowStockThreshold}), " &
                    $"critical at or below {criticalThreshold} (was {current.CriticalStockThreshold}), " &
                    $"expiring soon within {expiringSoonDays} days (was {current.ExpiringSoonDays})")
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
    End Sub

    Public Shared Sub SaveAppName(name As String)
        Dim value = If(name, "").Trim()
        If value.Length = 0 Then Throw New InputValidationException("appName", "Application name is required.")
        If value.Length > 40 Then Throw New InputValidationException("appName", "Application name must be 40 characters or fewer.")

        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                Dim current = SettingsRepository.Load(conn, tx)
                SettingsRepository.Save(conn, tx, SettingsRepository.AppNameKey, value)
                ActivityRepository.Log(conn, tx, "Application name", ActivityTypes.SettingsUpdated, $"Renamed ""{current.AppName}"" → ""{value}""")
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
    End Sub
End Class

Public NotInheritable Class LookupService
    Private Sub New()
    End Sub

    Public Shared Function GetAll(kind As LookupKind) As List(Of LookupItem)
        Using conn = Db.Open()
            Return LookupRepository.GetAll(conn, kind)
        End Using
    End Function

    Private Shared Function Noun(kind As LookupKind) As String
        Return If(kind = LookupKind.Category, "Category", "Unit")
    End Function

    Public Shared Sub Add(kind As LookupKind, name As String)
        Dim value = If(name, "").Trim()
        Dim maxLength = If(kind = LookupKind.Category, 80, 40)
        If value.Length = 0 Then Throw New InputValidationException("name", $"{Noun(kind)} name is required.")
        If value.Length > maxLength Then Throw New InputValidationException("name", $"{Noun(kind)} name must be {maxLength} characters or fewer.")

        Try
            Using conn = Db.Open()
                Using tx = conn.BeginTransaction()
                    If LookupRepository.FindIdByName(conn, tx, kind, value).HasValue Then
                        Throw New InputValidationException("name", $"{Noun(kind)} ""{value}"" already exists.")
                    End If
                    LookupRepository.Insert(conn, tx, kind, value)
                    ActivityRepository.Log(conn, tx, value,
                        If(kind = LookupKind.Category, ActivityTypes.CategoryAdded, ActivityTypes.UnitAdded),
                        $"Added {Noun(kind).ToLowerInvariant()}")
                    tx.Commit()
                End Using
            End Using
        Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
            Throw New InputValidationException("name", $"{Noun(kind)} ""{value}"" already exists.")
        End Try
        AppEvents.RaiseDataChanged()
    End Sub

    Public Shared Sub Remove(kind As LookupKind, id As Integer)
        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                Dim name = LookupRepository.GetName(conn, tx, kind, id)
                If name Is Nothing Then Throw New BusinessRuleException($"This {Noun(kind).ToLowerInvariant()} no longer exists.")
                Dim used = LookupRepository.CountUsage(conn, tx, kind, id)
                If used > 0 Then
                    Throw New BusinessRuleException($"""{name}"" is used by {Fmt.Plural(used, "item")}. Change those items first.")
                End If
                LookupRepository.Delete(conn, tx, kind, id)
                ActivityRepository.Log(conn, tx, name,
                    If(kind = LookupKind.Category, ActivityTypes.CategoryRemoved, ActivityTypes.UnitRemoved),
                    $"Removed {Noun(kind).ToLowerInvariant()}")
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
    End Sub
End Class

Public NotInheritable Class ActivityService
    Private Sub New()
    End Sub

    Public Shared Function GetRecent(limit As Integer) As List(Of ActivityEntry)
        Using conn = Db.Open()
            Return ActivityRepository.GetRecent(conn, limit)
        End Using
    End Function

    Public Shared Function Search(fromDate As Date?, toDate As Date?, activityType As String, text As String, limit As Integer) As List(Of ActivityEntry)
        Using conn = Db.Open()
            Return ActivityRepository.Search(conn, fromDate, toDate, activityType, text, limit)
        End Using
    End Function

    Public Shared Function GetTypes() As List(Of String)
        Using conn = Db.Open()
            Return ActivityRepository.GetTypes(conn)
        End Using
    End Function

    Public Shared Function GetLatestOfType(activityType As String) As ActivityEntry
        Using conn = Db.Open()
            Return ActivityRepository.GetLatestOfType(conn, activityType)
        End Using
    End Function
End Class
