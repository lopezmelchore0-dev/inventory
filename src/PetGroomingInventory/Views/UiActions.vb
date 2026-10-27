''' <summary>
''' User actions shared by several pages (dashboard tiles, table row buttons, toolbars),
''' so every entry point behaves the same: dialog → service → toast.
''' </summary>
Public NotInheritable Class UiActions
    Private Sub New()
    End Sub

    Private Shared Sub Toast(message As String, Optional kind As ToastKind = ToastKind.Success)
        MainWindow.Instance?.ShowToast(message, kind)
    End Sub

    Private Shared Sub ShowFailure(title As String, ex As Exception)
        If ErrorText.IsConnectionError(ex) Then
            MainWindow.Instance?.ShowDbError(ex)
        Else
            Dialogs.ShowError(title, ErrorText.Describe(ex))
        End If
    End Sub

    ' ---- Items ----

    Public Shared Sub AddItem()
        Dim editor As New ItemEditorWindow(Nothing)
        If Dialogs.ShowModal(editor) Then Toast($"""{editor.SavedName}"" was added to inventory.")
    End Sub

    Public Shared Sub EditItem(item As Item)
        If item Is Nothing Then Return
        Dim editor As New ItemEditorWindow(item)
        If Dialogs.ShowModal(editor) Then
            Toast(If(editor.WasChanged, $"""{editor.SavedName}"" was updated.", "No changes to save."),
                  If(editor.WasChanged, ToastKind.Success, ToastKind.Info))
        End If
    End Sub

    Public Shared Sub DeleteItem(item As Item)
        If item Is Nothing Then Return
        Dim message = $"""{item.Name}"" (current stock: {item.QuantityText}) will be permanently removed from inventory." &
                      $"{vbLf}{vbLf}Its stock history and activity records are kept. This cannot be undone."
        If Not Dialogs.Confirm("Remove this item?", message, "Remove Item", destructive:=True) Then Return
        Try
            InventoryService.DeleteItem(item.Id)
            Toast($"""{item.Name}"" was removed from inventory.")
        Catch ex As Exception
            ShowFailure("Couldn't remove item", ex)
        End Try
    End Sub

    ' ---- Stock ----

    Public Shared Sub StockIn(Optional itemId As Integer? = Nothing)
        Dim window As New StockInWindow(itemId)
        If Not window.HasItems Then
            Dialogs.ShowInfo("No items yet", "Add an item to inventory before recording stock received.")
            Return
        End If
        If Dialogs.ShowModal(window) Then Toast(window.ResultMessage)
    End Sub

    Public Shared Sub AdjustStock(Optional itemId As Integer? = Nothing)
        Dim window As New StockAdjustmentWindow(itemId)
        If Not window.HasItems Then
            Dialogs.ShowInfo("No items yet", "Add an item to inventory before adjusting stock.")
            Return
        End If
        If Dialogs.ShowModal(window) Then Toast(window.ResultMessage)
    End Sub

    ' ---- Suppliers ----

    ''' <summary>Returns the new supplier's id, or Nothing if cancelled.</summary>
    Public Shared Function AddSupplier() As Integer?
        Dim editor As New SupplierEditorWindow(Nothing)
        If Not Dialogs.ShowModal(editor) Then Return Nothing
        Toast($"Supplier ""{editor.SavedName}"" was added.")
        Return editor.SavedId
    End Function

    Public Shared Sub EditSupplier(supplier As Supplier)
        If supplier Is Nothing Then Return
        Dim editor As New SupplierEditorWindow(supplier)
        If Dialogs.ShowModal(editor) Then
            Toast(If(editor.WasChanged, $"Supplier ""{editor.SavedName}"" was updated.", "No changes to save."),
                  If(editor.WasChanged, ToastKind.Success, ToastKind.Info))
        End If
    End Sub

    Public Shared Sub DeleteSupplier(supplier As Supplier)
        If supplier Is Nothing Then Return
        If supplier.ItemCount > 0 Then
            Dialogs.ShowInfo("Supplier is in use",
                $"""{supplier.Name}"" is the supplier of {Fmt.Plural(supplier.ItemCount, "item")}. " &
                "Assign those items to another supplier (or remove them) before deleting this supplier.", MessageKind.Warning)
            Return
        End If
        If Not Dialogs.Confirm("Delete this supplier?",
                               $"""{supplier.Name}"" will be permanently deleted. Past stock records keep the supplier's name. This cannot be undone.",
                               "Delete Supplier", destructive:=True) Then Return
        Try
            SupplierService.Delete(supplier.Id)
            Toast($"Supplier ""{supplier.Name}"" was deleted.")
        Catch ex As Exception
            ShowFailure("Couldn't delete supplier", ex)
        End Try
    End Sub
End Class
