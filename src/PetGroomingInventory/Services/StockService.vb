Imports MySqlConnector

''' <summary>Stock-in and stock adjustments: the only ways an item's quantity changes after it is added.</summary>
Public NotInheritable Class StockService
    Private Sub New()
    End Sub

    Public Const OtherReason As String = "Other"

    Public Shared ReadOnly AdjustmentReasons As String() = {
        "Count correction",
        "Damaged",
        "Expired / disposed",
        "Used in grooming services",
        "Lost / missing",
        "Returned to supplier",
        OtherReason
    }

    Public Shared Function ComputeNewQuantity(current As Decimal, mode As AdjustmentMode, amount As Decimal) As Decimal
        Select Case mode
            Case AdjustmentMode.Add : Return current + amount
            Case AdjustmentMode.Deduct : Return current - amount
            Case Else : Return amount
        End Select
    End Function

    ''' <summary>Validates an adjustment against the current quantity. Keys: amount, reason, note.</summary>
    Public Shared Function ValidateAdjustment(current As Decimal, mode As AdjustmentMode, amount As Decimal,
                                              reason As String, note As String) As Dictionary(Of String, String)
        Dim errors As New Dictionary(Of String, String)
        Dim amountError = InputValidator.CheckAmount(amount, If(mode = AdjustmentMode.SetCount, "Counted quantity", "Quantity"),
                                                     allowZero:=(mode = AdjustmentMode.SetCount))
        If amountError IsNot Nothing Then
            errors("amount") = amountError
        Else
            Dim newQty = ComputeNewQuantity(current, mode, amount)
            If newQty < 0 Then
                errors("amount") = $"You can deduct at most {Fmt.Qty(current)} (the available stock)."
            ElseIf newQty = current Then
                errors("amount") = "This adjustment does not change the stock."
            ElseIf newQty > InputValidator.MaxAmount Then
                errors("amount") = $"Stock cannot exceed {InputValidator.MaxAmount:N0}."
            End If
        End If
        If String.IsNullOrWhiteSpace(reason) OrElse Not AdjustmentReasons.Contains(reason) Then
            errors("reason") = "Select a reason for this adjustment."
        ElseIf reason = OtherReason AndAlso String.IsNullOrWhiteSpace(note) Then
            errors("note") = "Describe the reason when you choose ""Other""."
        End If
        If note IsNot Nothing AndAlso note.Trim().Length > 255 Then errors("note") = "Note must be 255 characters or fewer."
        Return errors
    End Function

    Public Shared Sub StockIn(itemId As Integer, quantity As Decimal, receivedDate As Date, supplierId As Integer?, note As String)
        Dim errors As New Dictionary(Of String, String)
        If itemId <= 0 Then errors("item") = "Select the item you received."
        Dim qtyError = InputValidator.CheckAmount(quantity, "Quantity received", allowZero:=False)
        If qtyError IsNot Nothing Then errors("quantity") = qtyError
        If receivedDate.Date > Date.Today Then
            errors("date") = "The date received cannot be in the future."
        ElseIf receivedDate.Date < Date.Today.AddYears(-2) Then
            errors("date") = "The date received is more than 2 years ago. Check the date."
        End If
        If note IsNot Nothing AndAlso note.Trim().Length > 255 Then errors("note") = "Note must be 255 characters or fewer."
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                Dim item = ItemRepository.GetById(conn, tx, itemId)
                Dim current = ItemRepository.LockQuantity(conn, tx, itemId)
                If item Is Nothing OrElse Not current.HasValue Then Throw New InputValidationException("item", "This item no longer exists.")

                Dim supplierName As String = Nothing
                If supplierId.HasValue Then
                    Dim supplier = SupplierRepository.GetById(conn, tx, supplierId.Value)
                    If supplier Is Nothing Then Throw New InputValidationException("supplier", "The selected supplier no longer exists.")
                    supplierName = supplier.Name
                End If

                Dim newQty = current.Value + quantity
                If newQty > InputValidator.MaxAmount Then
                    Throw New InputValidationException("quantity", $"Stock cannot exceed {InputValidator.MaxAmount:N0}.")
                End If

                ItemRepository.UpdateQuantity(conn, tx, itemId, newQty)
                StockRepository.Insert(conn, tx, New StockTransaction With {
                    .ItemId = itemId,
                    .ItemName = item.Name,
                    .TxnType = TxnTypes.StockIn,
                    .QtyChange = quantity,
                    .QtyBefore = current.Value,
                    .QtyAfter = newQty,
                    .SupplierId = supplierId,
                    .SupplierName = supplierName,
                    .TxnDate = receivedDate.Date,
                    .Note = note
                })

                Dim description = $"Received {Fmt.Qty(quantity)}"
                If supplierName IsNot Nothing Then description &= $" from {supplierName}"
                description &= $" ({Fmt.Qty(current.Value)} → {Fmt.Qty(newQty)})"
                If receivedDate.Date <> Date.Today Then description &= $", dated {Fmt.DateText(receivedDate)}"
                If Not String.IsNullOrWhiteSpace(note) Then description &= $". Note: {note.Trim()}"
                ActivityRepository.Log(conn, tx, item.Name, ActivityTypes.StockReceived, description)
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
    End Sub

    ''' <summary>Applies an adjustment and returns the new quantity.</summary>
    Public Shared Function Adjust(itemId As Integer, mode As AdjustmentMode, amount As Decimal, reason As String, note As String) As Decimal
        If itemId <= 0 Then Throw New InputValidationException("item", "Select the item to adjust.")
        Dim newQty As Decimal

        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                Dim item = ItemRepository.GetById(conn, tx, itemId)
                Dim current = ItemRepository.LockQuantity(conn, tx, itemId)
                If item Is Nothing OrElse Not current.HasValue Then Throw New InputValidationException("item", "This item no longer exists.")

                Dim errors = ValidateAdjustment(current.Value, mode, amount, reason, note)
                If errors.Count > 0 Then Throw New InputValidationException(errors)

                newQty = ComputeNewQuantity(current.Value, mode, amount)
                Dim change = newQty - current.Value
                ItemRepository.UpdateQuantity(conn, tx, itemId, newQty)
                StockRepository.Insert(conn, tx, New StockTransaction With {
                    .ItemId = itemId,
                    .ItemName = item.Name,
                    .TxnType = TxnTypes.Adjustment,
                    .QtyChange = change,
                    .QtyBefore = current.Value,
                    .QtyAfter = newQty,
                    .TxnDate = Date.Today,
                    .Reason = reason,
                    .Note = note
                })

                Dim verb = If(change > 0, "Added", "Deducted")
                Dim description = $"{verb} {Fmt.Qty(Math.Abs(change))} — {reason} ({Fmt.Qty(current.Value)} → {Fmt.Qty(newQty)})"
                If Not String.IsNullOrWhiteSpace(note) Then description &= $". Note: {note.Trim()}"
                ActivityRepository.Log(conn, tx, item.Name, ActivityTypes.StockAdjusted, description)
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
        Return newQty
    End Function

    Public Shared Function GetHistory(adjustments As Boolean, Optional limit As Integer = 500) As List(Of StockTransaction)
        Using conn = Db.Open()
            Return StockRepository.GetHistory(conn, adjustments, limit)
        End Using
    End Function
End Class
