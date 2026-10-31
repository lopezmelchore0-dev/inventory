Class StockAdjustmentWindow

    Private ReadOnly _errors As FormErrors
    Private ReadOnly _items As List(Of Item)
    Private ReadOnly _settings As AppSettings
    Private _ready As Boolean

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
        _errors.Register("amount", AmountError, AmountBox)
        _errors.Register("reason", ReasonError, ReasonBox)
        _errors.Register("note", NoteError, NoteBox)

        Dim snapshot = InventoryService.LoadSnapshot()
        _items = snapshot.Items
        _settings = snapshot.Settings
        ItemSearchBox.Attach(ItemBox, _items)
        ReasonBox.ItemsSource = StockService.AdjustmentReasons
        If preselectItemId.HasValue Then ItemBox.SelectedValue = preselectItemId.Value

        _ready = True
        UpdateForm()
        AddHandler Loaded, Sub(s, e)
                               If ItemBox.SelectedItem Is Nothing Then ItemBox.Focus() Else AmountBox.Focus()
                           End Sub
    End Sub

    Private ReadOnly Property SelectedItem As Item
        Get
            Return TryCast(ItemBox.SelectedItem, Item)
        End Get
    End Property

    Private ReadOnly Property Mode As AdjustmentMode
        Get
            If ModeAdd.IsChecked.GetValueOrDefault() Then Return AdjustmentMode.Add
            If ModeSet.IsChecked.GetValueOrDefault() Then Return AdjustmentMode.SetCount
            Return AdjustmentMode.Deduct
        End Get
    End Property

    Private ReadOnly Property Reason As String
        Get
            Return TryCast(ReasonBox.SelectedItem, String)
        End Get
    End Property

    Private Sub Input_Changed(sender As Object, e As RoutedEventArgs)
        UpdateForm()
    End Sub

    ''' <summary>Updates labels, the current-stock panel and the before → after preview.</summary>
    Private Sub UpdateForm()
        If Not _ready Then Return
        Dim item = SelectedItem

        Select Case Mode
            Case AdjustmentMode.Add : AmountLabel.Text = "Quantity to add *"
            Case AdjustmentMode.SetCount : AmountLabel.Text = "Counted quantity (actual stock on hand) *"
            Case Else : AmountLabel.Text = "Quantity to deduct *"
        End Select
        NoteLabel.Text = If(Reason = StockService.OtherReason, "Note * (describe the reason)", "Note (optional)")

        If item Is Nothing Then
            CurrentPanel.Visibility = Visibility.Collapsed
            PreviewPanel.Visibility = Visibility.Collapsed
            Return
        End If
        CurrentPanel.Visibility = Visibility.Visible
        CurrentText.Text = item.QuantityText
        CurrentDetails.Text = item.PickerDetails
        CurrentBadge.Content = item

        Dim amount As Decimal
        If InputValidator.TryParseAmount(AmountBox.Text, amount) AndAlso amount >= 0 Then
            Dim newQty = StockService.ComputeNewQuantity(item.Quantity, Mode, amount)
            If newQty < 0 Then
                PreviewText.Text = $"Cannot deduct {Fmt.Qty(amount)} — only {item.QuantityText} available."
            Else
                Dim newStatus = StatusCalculator.GetStockStatus(newQty, _settings.LowStockThreshold, _settings.CriticalStockThreshold)
                PreviewText.Text = $"{Fmt.Qty(item.Quantity)} → {Fmt.Qty(newQty)}  ({Fmt.SignedQty(newQty - item.Quantity)})" &
                                   $"  ·  Status after: {StatusCalculator.StockLabel(newStatus)}"
            End If
            PreviewPanel.Visibility = Visibility.Visible
        Else
            PreviewPanel.Visibility = Visibility.Collapsed
        End If
    End Sub

    Private Sub Save_Click(sender As Object, e As RoutedEventArgs)
        _errors.Clear()
        Dim item = SelectedItem
        If item Is Nothing Then
            _errors.Show(New Dictionary(Of String, String) From {{"item", "Select the item to adjust."}})
            Return
        End If

        Dim amount As Decimal
        Dim errors As New Dictionary(Of String, String)
        If String.IsNullOrWhiteSpace(AmountBox.Text) Then
            errors("amount") = "Enter a quantity."
        ElseIf Not InputValidator.TryParseAmount(AmountBox.Text, amount) Then
            errors("amount") = "Enter a valid quantity using numbers only (e.g. 3 or 2.5)."
        End If
        For Each kv In StockService.ValidateAdjustment(item.Quantity, Mode, amount, Reason, NoteBox.Text)
            If Not errors.ContainsKey(kv.Key) Then errors(kv.Key) = kv.Value
        Next
        If errors.Count > 0 Then
            _errors.Show(errors)
            Return
        End If

        Dim newQty = StockService.ComputeNewQuantity(item.Quantity, Mode, amount)
        Dim message = $"""{item.Name}""{vbLf}{vbLf}Stock: {Fmt.Qty(item.Quantity)} → {Fmt.Qty(newQty)} ({Fmt.SignedQty(newQty - item.Quantity)}){vbLf}" &
                      $"Reason: {Reason}" & If(String.IsNullOrWhiteSpace(NoteBox.Text), "", $"{vbLf}Note: {NoteBox.Text.Trim()}")
        If Not Dialogs.Confirm("Confirm stock adjustment", message, "Apply Adjustment") Then Return

        Try
            Dim result = StockService.Adjust(item.Id, Mode, amount, Reason, NoteBox.Text)
            ResultMessage = $"Stock of ""{item.Name}"" adjusted to {Fmt.Qty(result)}."
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
