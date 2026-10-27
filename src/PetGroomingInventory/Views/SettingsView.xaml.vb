Imports System.Globalization
Imports Microsoft.Win32

Class SettingsView

    Private ReadOnly _ruleErrors As FormErrors
    Private ReadOnly _appNameErrors As FormErrors
    Private _loading As Boolean

    Public Sub New()
        InitializeComponent()
        _ruleErrors = New FormErrors(Nothing)
        _ruleErrors.Register("low", LowError, LowBox)
        _ruleErrors.Register("critical", CriticalError, CriticalBox)
        _ruleErrors.Register("days", DaysError, DaysBox)
        _appNameErrors = New FormErrors(Nothing)
        _appNameErrors.Register("appName", AppNameError, AppNameBox)
    End Sub

    Protected Overrides Sub LoadData()
        _loading = True
        Try
            Dim settings = SettingsService.GetSettings()
            LowBox.Text = settings.LowStockThreshold.ToString(CultureInfo.InvariantCulture)
            CriticalBox.Text = settings.CriticalStockThreshold.ToString(CultureInfo.InvariantCulture)
            DaysBox.Text = settings.ExpiringSoonDays.ToString(CultureInfo.InvariantCulture)
            AppNameBox.Text = settings.AppName
        Finally
            _loading = False
        End Try
        UpdateLegend()

        CategoryList.ItemsSource = LookupService.GetAll(LookupKind.Category)

        Dim lastBackup = ActivityService.GetLatestOfType(ActivityTypes.BackupCreated)
        LastBackupText.Text = If(lastBackup Is Nothing, "No backup has been created yet.",
                                 $"Last backup: {lastBackup.CreatedAtText} ({lastBackup.SubjectName})")

        DbInfoText.Text = $"MySQL at {Db.Config.Server}:{Db.Config.Port} · database ""{Db.Config.Database}"" · user ""{Db.Config.User}"""
        DbFileText.Text = $"Change these in {DbConfig.FilePath}"

        Dim samples = SampleDataService.CountSampleItems()
        SampleStatusText.Text = If(samples = 0, "No sample records are currently loaded.", $"{Fmt.Plural(samples, "sample record")} currently loaded.")
    End Sub

    ' ---- Stock rules ----

    Private Sub Rules_TextChanged(sender As Object, e As TextChangedEventArgs)
        If Not _loading Then UpdateLegend()
    End Sub

    Private Function ReadRules(ByRef low As Integer, ByRef critical As Integer, ByRef days As Integer) As Dictionary(Of String, String)
        Dim errors As New Dictionary(Of String, String)
        If Not InputValidator.TryParseWholeNumber(LowBox.Text, low) Then errors("low") = "Enter a whole number."
        If Not InputValidator.TryParseWholeNumber(CriticalBox.Text, critical) Then errors("critical") = "Enter a whole number."
        If Not InputValidator.TryParseWholeNumber(DaysBox.Text, days) Then errors("days") = "Enter a whole number of days."
        If errors.Count = 0 Then
            For Each kv In StatusCalculator.ValidateRules(low, critical, days)
                errors(kv.Key) = kv.Value
            Next
        End If
        Return errors
    End Function

    Private Sub UpdateLegend()
        Dim low, critical, days As Integer
        If ReadRules(low, critical, days).Count > 0 Then
            InStockRule.Text = "—"
            LowRule.Text = "—"
            CriticalRule.Text = "—"
            Return
        End If
        InStockRule.Text = $"more than {low}"
        LowRule.Text = $"more than {critical}, up to {low}"
        CriticalRule.Text = $"more than 0, up to {critical}"
    End Sub

    Private Sub SaveRules_Click(sender As Object, e As RoutedEventArgs)
        _ruleErrors.Clear()
        Dim low, critical, days As Integer
        Dim errors = ReadRules(low, critical, days)
        If errors.Count > 0 Then
            _ruleErrors.Show(errors)
            Return
        End If
        Try
            SettingsService.SaveStockRules(low, critical, days)
            MainWindow.Instance?.ShowToast("Stock status rules saved. All statuses have been updated.")
        Catch ex As InputValidationException
            _ruleErrors.Show(ex.Errors)
        Catch ex As Exception
            Dialogs.ShowError("Couldn't save the rules", ErrorText.Describe(ex))
        End Try
    End Sub

    ' ---- App name ----

    Private Sub SaveAppName_Click(sender As Object, e As RoutedEventArgs)
        _appNameErrors.Clear()
        Try
            SettingsService.SaveAppName(AppNameBox.Text)
            MainWindow.Instance?.ShowToast("Application name saved.")
        Catch ex As InputValidationException
            _appNameErrors.Show(ex.Errors)
        Catch ex As Exception
            Dialogs.ShowError("Couldn't save the name", ErrorText.Describe(ex))
        End Try
    End Sub

    ' ---- Categories ----

    Private Sub AddLookup(kind As LookupKind, box As TextBox, errorLabel As TextBlock)
        errorLabel.Visibility = Visibility.Collapsed
        box.ClearValue(Control.BorderBrushProperty)
        Try
            LookupService.Add(kind, box.Text)
            MainWindow.Instance?.ShowToast($"Category ""{box.Text.Trim()}"" added.")
            box.Text = ""
        Catch ex As InputValidationException
            errorLabel.Text = ex.Message
            errorLabel.Visibility = Visibility.Visible
            box.BorderBrush = StatusPalette.Find("ErrorTextBrush")
        Catch ex As Exception
            Dialogs.ShowError("Couldn't add", ErrorText.Describe(ex))
        End Try
    End Sub

    Private Sub AddCategory_Click(sender As Object, e As RoutedEventArgs)
        AddLookup(LookupKind.Category, NewCategoryBox, CategoryError)
    End Sub

    Private Sub NewCategoryBox_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then AddLookup(LookupKind.Category, NewCategoryBox, CategoryError)
    End Sub

    Private Sub RemoveLookup_Click(sender As Object, e As RoutedEventArgs)
        Dim button = DirectCast(sender, FrameworkElement)
        Dim entry = TryCast(button.DataContext, LookupItem)
        If entry Is Nothing Then Return
        Const kind = LookupKind.Category
        Const noun = "category"

        If entry.UsageCount > 0 Then
            Dialogs.ShowInfo("Still in use", $"""{entry.Name}"" is used by {Fmt.Plural(entry.UsageCount, "item")}. Change those items first.", MessageKind.Warning)
            Return
        End If
        If Not Dialogs.Confirm($"Remove this {noun}?", $"""{entry.Name}"" will be removed from the {noun} list.", "Remove", destructive:=True) Then Return
        Try
            LookupService.Remove(kind, entry.Id)
            MainWindow.Instance?.ShowToast($"""{entry.Name}"" removed.")
        Catch ex As Exception
            Dialogs.ShowError("Couldn't remove", ErrorText.Describe(ex))
        End Try
    End Sub

    ' ---- Backup and restore ----

    Private Sub Backup_Click(sender As Object, e As RoutedEventArgs)
        Dim dialog As New SaveFileDialog With {
            .Title = "Save backup",
            .Filter = "PawStock backup (*.json)|*.json",
            .FileName = BackupService.SuggestedFileName(),
            .InitialDirectory = BackupService.EnsureDefaultFolder(),
            .AddExtension = True,
            .OverwritePrompt = True
        }
        If Not dialog.ShowDialog(Window.GetWindow(Me)).GetValueOrDefault() Then Return

        Try
            Mouse.OverrideCursor = Cursors.Wait
            Dim result = BackupService.CreateBackup(dialog.FileName)
            Mouse.OverrideCursor = Nothing
            Dialogs.ShowInfo("Backup created",
                $"Your data was backed up successfully.{vbLf}{vbLf}" &
                $"• {Fmt.Plural(result.ItemCount, "item")}{vbLf}• {Fmt.Plural(result.SupplierCount, "supplier")}{vbLf}" &
                $"• {Fmt.Plural(result.TransactionCount, "stock record")}{vbLf}• {Fmt.Plural(result.ActivityCount, "activity record")}{vbLf}{vbLf}" &
                $"Saved to: {result.FilePath}", MessageKind.Success)
        Catch ex As Exception
            Mouse.OverrideCursor = Nothing
            Dialogs.ShowError("Backup failed", ErrorText.Describe(ex))
        End Try
    End Sub

    Private Sub Restore_Click(sender As Object, e As RoutedEventArgs)
        Dim dialog As New OpenFileDialog With {
            .Title = "Choose a backup to restore",
            .Filter = "PawStock backup (*.json)|*.json|All files (*.*)|*.*",
            .InitialDirectory = BackupService.EnsureDefaultFolder(),
            .CheckFileExists = True
        }
        If Not dialog.ShowDialog(Window.GetWindow(Me)).GetValueOrDefault() Then Return

        Dim file As BackupFile
        Try
            file = BackupService.ReadBackup(dialog.FileName)
        Catch ex As Exception
            Dialogs.ShowError("Invalid backup file", ErrorText.Describe(ex))
            Return
        End Try

        Dim problems = BackupService.Validate(file)
        If problems.Count > 0 Then
            Dialogs.ShowError("Invalid backup file",
                "This file cannot be restored. Your current data has not been changed." & vbLf & vbLf & "• " & String.Join(vbLf & "• ", problems.Take(6)))
            Return
        End If

        Dim d = file.Data
        Dim message = $"Backup created {Fmt.DateTimeText(file.CreatedAt)} contains:{vbLf}" &
                      $"• {Fmt.Plural(d.Items.Count, "item")}{vbLf}• {Fmt.Plural(d.Suppliers.Count, "supplier")}{vbLf}" &
                      $"• {Fmt.Plural(d.StockTransactions.Count, "stock record")}{vbLf}• {Fmt.Plural(d.Activities.Count, "activity record")}{vbLf}{vbLf}" &
                      "ALL current inventory, supplier, stock history and activity records will be replaced by this backup. " &
                      "A safety backup of your current data will be saved first."
        If Not Dialogs.Confirm("Replace current data with this backup?", message, "Restore Backup", destructive:=True) Then Return

        Try
            Mouse.OverrideCursor = Cursors.Wait
            Dim safetyPath = BackupService.Restore(file)
            Mouse.OverrideCursor = Nothing
            Dialogs.ShowInfo("Restore complete",
                $"Your data has been restored from the backup.{vbLf}{vbLf}A safety copy of the previous data was saved to:{vbLf}{safetyPath}",
                MessageKind.Success)
        Catch ex As Exception
            Mouse.OverrideCursor = Nothing
            Dialogs.ShowError("Restore failed", "No data was changed. " & ErrorText.Describe(ex))
        End Try
    End Sub

    Private Sub OpenBackupFolder_Click(sender As Object, e As RoutedEventArgs)
        Try
            Process.Start(New ProcessStartInfo("explorer.exe", $"""{BackupService.EnsureDefaultFolder()}""") With {.UseShellExecute = True})
        Catch ex As Exception
            Dialogs.ShowError("Couldn't open the folder", ex.Message)
        End Try
    End Sub

    ' ---- Sample data ----

    Private Sub LoadSamples_Click(sender As Object, e As RoutedEventArgs)
        If Not Dialogs.Confirm("Load sample data?",
                               "About 12 sample items and 3 sample suppliers will be added, all tagged SAMPLE. You can remove them later with one click.",
                               "Load Sample Data") Then Return
        Try
            MainWindow.Instance?.ShowToast(SampleDataService.Load())
        Catch ex As Exception
            Dialogs.ShowError("Couldn't load sample data", ErrorText.Describe(ex))
        End Try
    End Sub

    Private Sub RemoveSamples_Click(sender As Object, e As RoutedEventArgs)
        If Not Dialogs.Confirm("Remove sample data?",
                               "All items and suppliers tagged SAMPLE will be permanently removed. Your own records are not affected.",
                               "Remove Sample Data", destructive:=True) Then Return
        Try
            MainWindow.Instance?.ShowToast(SampleDataService.Remove())
        Catch ex As Exception
            Dialogs.ShowError("Couldn't remove sample data", ErrorText.Describe(ex))
        End Try
    End Sub
End Class
