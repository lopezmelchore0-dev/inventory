Class StockInWindow

    Private ReadOnly _errors As FormErrors
    Private ReadOnly _items As List(Of Item)

    Public ReadOnly Property HasItems As Boolean
        Get
            Return _items.Count > 0
        End Get
    End Property

    Public Property ResultMessage As String

    Public Sub New(preselectItemId As Integer?)
        InitializeComponent()
        _errors = New FormErrors(GeneralError)
        _errors.Register("item", ItemError, ItemBox)
        _errors.Register("quantity", QuantityError, QuantityBox)
        _errors.Register("unit", UnitError, UnitBox)
        _errors.Register("date", DateError, ReceivedDatePicker)
        _errors.Register("supplier", SupplierError, SupplierBox)
        _errors.Register("note", NoteError, NoteBox)

        _items = InventoryService.GetItems()
        ItemSearchBox.Attach(ItemBox, _items)

        ' First entry lets the user record stock without a supplier (e.g. a transfer or donation).
        Dim suppliers As New List(Of Supplier) From {New Supplier With {.Id = 0, .Name = "— Not specified —"}}
        suppliers.AddRange(SupplierService.GetAll())
        SupplierBox.ItemsSource = suppliers
        SupplierBox.SelectedIndex = 0

        ReceivedDatePicker.SelectedDate = Date.Today
        ReceivedDatePicker.DisplayDateEnd = Date.Today

        If preselectItemId.HasValue Then ItemBox.SelectedValue = preselectItemId.Value
        AddHandler Loaded, Sub(s, e)
                               If ItemBox.SelectedItem Is Nothing Then ItemBox.Focus() Else QuantityBox.Focus()
                           End Sub
    End Sub

    Private ReadOnly Property SelectedItem As Item
        Get
            Return TryCast(ItemBox.SelectedItem, Item)
        End Get
    End Property

    Private Sub ItemBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim item = SelectedItem
        StockUnitField.Refresh(UnitBox, UnitError, item, QuantityBox.Text)
        If item Is Nothing Then
            CurrentPanel.Visibility = Visibility.Collapsed
        Else
            CurrentPanel.Visibility = Visibility.Visible
            CurrentText.Text = item.QuantityText
            CurrentDetails.Text = item.PickerDetails
            CurrentBadge.Content = item
            SupplierBox.SelectedValue = item.SupplierId
        End If
        UpdatePreview()
    End Sub

    Private Sub QuantityBox_TextChanged(sender As Object, e As TextChangedEventArgs)
        StockUnitField.Refresh(UnitBox, UnitError, SelectedItem, QuantityBox.Text)
        UpdatePreview()
    End Sub

    Private Sub UpdatePreview()
        Dim item = SelectedItem
        Dim qty As Decimal
        If item IsNot Nothing AndAlso InputValidator.TryParseAmount(QuantityBox.Text, qty) AndAlso qty > 0 Then
            PreviewText.Text = $"New stock after receiving: {Fmt.Qty(item.Quantity + qty, item.Unit)}  (was {Fmt.Qty(item.Quantity, item.Unit)})"
            PreviewPanel.Visibility = Visibility.Visible
        Else
            PreviewPanel.Visibility = Visibility.Collapsed
        End If
    End Sub

    Private Sub Save_Click(sender As Object, e As RoutedEventArgs)
        _errors.Clear()
        Dim errors As New Dictionary(Of String, String)
        Dim item = SelectedItem
        If item Is Nothing Then
            errors("item") = "Select the item you received."
        Else
            Dim unitError = StockService.CheckUnit(item.Unit, item.Unit)
            If unitError IsNot Nothing Then errors("unit") = unitError
        End If

        Dim qty As Decimal
        If String.IsNullOrWhiteSpace(QuantityBox.Text) Then
            errors("quantity") = "Enter the quantity received."
        ElseIf Not InputValidator.TryParseAmount(QuantityBox.Text, qty) Then
            errors("quantity") = "Enter a valid quantity using numbers only (e.g. 24 or 2.5)."
        Else
            Dim amountError = If(InputValidator.CheckAmount(qty, "Quantity received", allowZero:=False),
                                 InputValidator.CheckWholeForUnit(qty, item?.Unit, "Quantity received"))
            If amountError IsNot Nothing Then errors("quantity") = amountError
        End If

        If Not ReceivedDatePicker.SelectedDate.HasValue Then errors("date") = "Select the date the stock was received."

        If errors.Count > 0 Then
            _errors.Show(errors)
            Return
        End If

        Dim supplierId = CInt(If(SupplierBox.SelectedValue, 0))
        Try
            StockService.StockIn(item.Id, qty, ReceivedDatePicker.SelectedDate.Value, If(supplierId > 0, supplierId, CType(Nothing, Integer?)), NoteBox.Text,
                                 expectedUnit:=item.Unit)
            ResultMessage = $"Received {Fmt.Qty(qty, item.Unit)} of ""{item.Name}"". New stock: {Fmt.Qty(item.Quantity + qty, item.Unit)}."
            DialogResult = True
        Catch ex As InputValidationException
            _errors.Show(ex.Errors)
        Catch ex As Exception
            _errors.ShowGeneral(ErrorText.Describe(ex))
        End Try
    End Sub

    Private Sub Cancel_Click(sender As Object, e As RoutedEventArgs)
        DialogResult = False
    End Sub

    Private Sub Header_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        DragMove()
    End Sub
End Class
