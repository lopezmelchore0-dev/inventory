Class SupplierEditorWindow

    Private ReadOnly _existing As Supplier
    Private ReadOnly _errors As FormErrors

    Public Property SavedId As Integer
    Public Property SavedName As String
    Public Property WasChanged As Boolean

    ''' <param name="existing">Nothing to add a new supplier; a supplier to edit.</param>
    Public Sub New(existing As Supplier)
        InitializeComponent()
        _existing = existing
        _errors = New FormErrors(GeneralError)
        _errors.Register("name", NameError, NameBox)
        _errors.Register("contact", ContactError, ContactBox)
        _errors.Register("phone", PhoneError, PhoneBox)
        _errors.Register("address", AddressError, AddressBox)
        _errors.Register("email", EmailError, EmailBox)

        If existing IsNot Nothing Then
            Title = "Edit Supplier"
            HeaderTitle.Text = "Edit Supplier"
            HeaderSubtitle.Text = "Changes are reflected on every item from this supplier."
            SaveButton.Content = "Save Changes"
            NameBox.Text = existing.Name
            ContactBox.Text = existing.ContactPerson
            PhoneBox.Text = existing.Phone
            AddressBox.Text = existing.Address
            EmailBox.Text = existing.Email
        End If
        AddHandler Loaded, Sub(s, e) NameBox.Focus()
    End Sub

    Private Sub Save_Click(sender As Object, e As RoutedEventArgs)
        _errors.Clear()
        Dim supplier As New Supplier With {
            .Id = If(_existing?.Id, 0),
            .Name = NameBox.Text.Trim(),
            .ContactPerson = ContactBox.Text.Trim(),
            .Phone = PhoneBox.Text.Trim(),
            .Address = AddressBox.Text.Trim(),
            .Email = EmailBox.Text.Trim()
        }
        Dim errors = SupplierService.Validate(supplier)
        If errors.Count > 0 Then
            _errors.Show(errors)
            Return
        End If

        Try
            If _existing Is Nothing Then
                SavedId = SupplierService.Add(supplier)
                WasChanged = True
            Else
                WasChanged = SupplierService.Update(supplier)
                SavedId = supplier.Id
            End If
            SavedName = supplier.Name
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
