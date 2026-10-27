''' <summary>Add Item / Edit Item form.</summary>
Class ItemEditorWindow

    Private ReadOnly _existing As Item
    Private ReadOnly _errors As FormErrors

    Public Property SavedName As String
    Public Property WasChanged As Boolean

    ''' <param name="existing">Nothing to add a new item; an item to edit its details.</param>
    Public Sub New(existing As Item)
        InitializeComponent()
        _existing = existing
        _errors = New FormErrors(GeneralError)
        _errors.Register("name", NameError, NameBox)
        _errors.Register("category", CategoryError, CategoryBox)
        _errors.Register("pettype", PetTypeError, PetTypeBox)
        _errors.Register("quantity", QuantityError, QuantityBox)
        _errors.Register("price", PriceError, PriceBox)
        _errors.Register("supplier", SupplierError, SupplierBox)
        _errors.Register("expiration", ExpirationError, ExpirationPicker)
        _errors.Register("description", DescriptionError, DescriptionBox)

        CategoryBox.ItemsSource = LookupService.GetAll(LookupKind.Category)
        PetTypeBox.ItemsSource = New List(Of FilterOption) From {
            New FilterOption("Dog", PetTypes.Dog),
            New FilterOption("Cat", PetTypes.Cat),
            New FilterOption("Both (Dog & Cat)", PetTypes.Both)
        }
        SuggestionBox.Attach(DescriptionBox, InventoryService.GetDescriptionSuggestions(), InventoryService.MaxDescriptionLength)
        LoadSuppliers(Nothing)
        UpdateExpirationHint()

        If existing Is Nothing Then
            Title = "Add Item"
        Else
            Title = "Edit Item"
            HeaderTitle.Text = "Edit Item"
            HeaderSubtitle.Text = "Update the details of this product."
            HeaderIcon.Text = ChrW(&HE70F)
            SaveButton.Content = "Save Changes"
            NameBox.Text = existing.Name
            CategoryBox.SelectedValue = existing.CategoryId
            PetTypeBox.SelectedValue = existing.PetType
            SupplierBox.SelectedValue = existing.SupplierId
            QuantityLabel.Text = "Available Quantity"
            QuantityBox.Text = Fmt.Qty(existing.Quantity)
            QuantityBox.IsReadOnly = True
            QuantityBox.Focusable = False
            QuantityHint.Visibility = Visibility.Visible
            PriceBox.Text = existing.RetailPrice.ToString("0.00", Globalization.CultureInfo.InvariantCulture)
            ExpirationPicker.SelectedDate = existing.ExpirationDate
            ' Shown as-is even when it is not one of the suggestions (custom text, or a long note from an older version).
            DescriptionBox.Text = If(existing.Description, "")
        End If
        AddHandler Loaded, Sub(s, e) NameBox.Focus()
    End Sub

    Private Sub LoadSuppliers(selectId As Integer?)
        Dim previous = selectId
        If Not previous.HasValue AndAlso SupplierBox.SelectedValue IsNot Nothing Then previous = CInt(SupplierBox.SelectedValue)
        Dim suppliers = SupplierService.GetAll()
        SupplierBox.ItemsSource = suppliers
        If previous.HasValue Then
            SupplierBox.SelectedValue = previous.Value
        ElseIf suppliers.Count = 1 Then
            SupplierBox.SelectedIndex = 0
        End If
        NoSupplierHint.Visibility = If(suppliers.Count = 0, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Shared Function SelectedId(combo As ComboBox) As Integer
        Return If(combo.SelectedValue Is Nothing, 0, CInt(combo.SelectedValue))
    End Function

    ''' <summary>Medicine, Vitamins and Pet Food &amp; Treats carry printed expiry dates, so the date is recommended for them.</summary>
    Private Sub UpdateExpirationHint()
        Dim category = TryCast(CategoryBox.SelectedItem, LookupItem)?.Name
        If ItemCategories.IsExpiryTracked(category) Then
            ExpirationLabel.Text = "Expiration Date (recommended)"
            ExpirationHint.Text = $"{category} usually has a printed expiration date. Add it so PawStock can alert you before it expires."
        Else
            ExpirationLabel.Text = "Expiration Date (optional)"
            ExpirationHint.Text = "Leave blank for products that do not expire. No expiration alerts are shown for them."
        End If
    End Sub

    Private Sub CategoryBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        UpdateExpirationHint()
    End Sub

    ' Quantity and price accept numbers only: digits, a decimal point and thousands commas.
    Private Sub Amount_PreviewTextInput(sender As Object, e As TextCompositionEventArgs)
        e.Handled = e.Text.Any(Function(c) Not (Char.IsDigit(c) OrElse c = "."c OrElse c = ","c))
    End Sub

    Private Sub Amount_Pasting(sender As Object, e As DataObjectPastingEventArgs)
        Dim text = TryCast(e.DataObject.GetData(DataFormats.UnicodeText), String)
        Dim value As Decimal
        If text Is Nothing OrElse Not InputValidator.TryParseAmount(text, value) OrElse value < 0D Then e.CancelCommand()
    End Sub

    Private Sub Save_Click(sender As Object, e As RoutedEventArgs)
        _errors.Clear()
        Dim errors As New Dictionary(Of String, String)

        Dim quantity As Decimal
        If _existing IsNot Nothing Then
            quantity = _existing.Quantity
        ElseIf String.IsNullOrWhiteSpace(QuantityBox.Text) Then
            errors("quantity") = "Quantity is required. Enter 0 if you have none in stock yet."
        ElseIf Not InputValidator.TryParseAmount(QuantityBox.Text, quantity) Then
            errors("quantity") = "Enter a valid quantity using numbers only (e.g. 12 or 2.5)."
        End If

        Dim price As Decimal
        If String.IsNullOrWhiteSpace(PriceBox.Text) Then
            errors("price") = "Retail price is required."
        ElseIf Not InputValidator.TryParseAmount(PriceBox.Text, price) Then
            errors("price") = "Enter a valid price using numbers only (e.g. 150 or 149.75)."
        End If

        If ExpirationPicker.SelectedDate Is Nothing AndAlso Not String.IsNullOrWhiteSpace(ExpirationPicker.Text) Then
            errors("expiration") = "Enter a valid date, or click Clear to leave it blank."
        End If

        Dim item As New Item With {
            .Id = If(_existing?.Id, 0),
            .Name = NameBox.Text.Trim(),
            .CategoryId = SelectedId(CategoryBox),
            .PetType = If(TryCast(PetTypeBox.SelectedValue, String), ""),
            .SupplierId = SelectedId(SupplierBox),
            .Quantity = quantity,
            .RetailPrice = price,
            .ExpirationDate = ExpirationPicker.SelectedDate?.Date,
            .Description = DescriptionBox.Text.Trim(),
            .IsSample = If(_existing?.IsSample, False)
        }

        ' Merge the service's rules so every problem is shown at once.
        For Each kv In InventoryService.Validate(item)
            If Not errors.ContainsKey(kv.Key) Then errors(kv.Key) = kv.Value
        Next
        If errors.Count > 0 Then
            _errors.Show(errors)
            Return
        End If

        Try
            If _existing Is Nothing Then
                InventoryService.AddItem(item)
                WasChanged = True
            Else
                WasChanged = InventoryService.UpdateItem(item)
            End If
            SavedName = item.Name
            DialogResult = True
        Catch ex As InputValidationException
            _errors.Show(ex.Errors)
        Catch ex As Exception
            _errors.ShowGeneral(ErrorText.Describe(ex))
        End Try
    End Sub

    Private Sub NewSupplier_Click(sender As Object, e As RoutedEventArgs)
        Dim newId = UiActions.AddSupplier()
        If newId.HasValue Then LoadSuppliers(newId)
    End Sub

    Private Sub ClearDate_Click(sender As Object, e As RoutedEventArgs)
        ExpirationPicker.SelectedDate = Nothing
        ExpirationPicker.Text = ""
    End Sub

    Private Sub Cancel_Click(sender As Object, e As RoutedEventArgs)
        DialogResult = False
    End Sub

    Private Sub Header_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        DragMove()
    End Sub
End Class
