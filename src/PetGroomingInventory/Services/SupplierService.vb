Imports MySqlConnector

Public NotInheritable Class SupplierService
    Private Sub New()
    End Sub

    Public Shared Function GetAll() As List(Of Supplier)
        Using conn = Db.Open()
            Return SupplierRepository.GetAll(conn)
        End Using
    End Function

    ''' <summary>Keys: name, contact, phone, address, email.</summary>
    Public Shared Function Validate(s As Supplier) As Dictionary(Of String, String)
        Dim errors As New Dictionary(Of String, String)
        CheckRequired(errors, "name", s.Name, "Supplier name", 120)
        CheckRequired(errors, "contact", s.ContactPerson, "Contact person", 120)
        CheckRequired(errors, "address", s.Address, "Address", 255)
        If String.IsNullOrWhiteSpace(s.Phone) Then
            errors("phone") = "Phone number is required."
        ElseIf Not InputValidator.IsValidPhone(s.Phone) Then
            errors("phone") = "Enter a valid phone number, e.g. 0917 123 4567 or (078) 888 1234."
        End If
        If Not String.IsNullOrWhiteSpace(s.Email) Then
            If s.Email.Trim().Length > 120 Then
                errors("email") = "Email must be 120 characters or fewer."
            ElseIf Not InputValidator.IsValidEmail(s.Email) Then
                errors("email") = "Enter a valid email address or leave it blank."
            End If
        End If
        Return errors
    End Function

    Private Shared Sub CheckRequired(errors As Dictionary(Of String, String), key As String, value As String, label As String, maxLength As Integer)
        Dim text = If(value, "").Trim()
        If text.Length = 0 Then
            errors(key) = $"{label} is required."
        ElseIf text.Length > maxLength Then
            errors(key) = $"{label} must be {maxLength} characters or fewer."
        End If
    End Sub

    Public Shared Function Add(s As Supplier) As Integer
        Dim errors = Validate(s)
        If errors.Count > 0 Then Throw New InputValidationException(errors)
        Dim newId As Integer
        Try
            Using conn = Db.Open()
                Using tx = conn.BeginTransaction()
                    If SupplierRepository.NameExists(conn, tx, s.Name, 0) Then
                        Throw New InputValidationException("name", "A supplier with this name already exists.")
                    End If
                    newId = SupplierRepository.Insert(conn, tx, s)
                    ActivityRepository.Log(conn, tx, s.Name.Trim(), ActivityTypes.SupplierAdded,
                                           $"Added supplier (contact: {s.ContactPerson.Trim()}, {s.Phone.Trim()})")
                    tx.Commit()
                End Using
            End Using
        Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
            Throw New InputValidationException("name", "A supplier with this name already exists.")
        End Try
        AppEvents.RaiseDataChanged()
        Return newId
    End Function

    ''' <summary>Returns False when nothing changed. Items reference suppliers by id, so a rename shows everywhere.</summary>
    Public Shared Function Update(s As Supplier) As Boolean
        Dim errors = Validate(s)
        If errors.Count > 0 Then Throw New InputValidationException(errors)
        Try
            Using conn = Db.Open()
                Using tx = conn.BeginTransaction()
                    Dim existing = SupplierRepository.GetById(conn, tx, s.Id)
                    If existing Is Nothing Then Throw New BusinessRuleException("This supplier no longer exists.")
                    If SupplierRepository.NameExists(conn, tx, s.Name, s.Id) Then
                        Throw New InputValidationException("name", "Another supplier already uses this name.")
                    End If

                    Dim changes As New List(Of String)
                    CompareField(changes, "name", existing.Name, s.Name)
                    CompareField(changes, "contact person", existing.ContactPerson, s.ContactPerson)
                    CompareField(changes, "phone", existing.Phone, s.Phone)
                    CompareField(changes, "address", existing.Address, s.Address)
                    CompareField(changes, "email", existing.Email, s.Email)
                    If changes.Count = 0 Then Return False

                    SupplierRepository.Update(conn, tx, s)
                    ActivityRepository.Log(conn, tx, s.Name.Trim(), ActivityTypes.SupplierUpdated, "Changed " & String.Join("; ", changes))
                    tx.Commit()
                End Using
            End Using
        Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
            Throw New InputValidationException("name", "Another supplier already uses this name.")
        End Try
        AppEvents.RaiseDataChanged()
        Return True
    End Function

    Private Shared Sub CompareField(changes As List(Of String), label As String, oldValue As String, newValue As String)
        Dim before = If(oldValue, "").Trim()
        Dim after = If(newValue, "").Trim()
        If before <> after Then
            changes.Add($"{label} ""{If(before.Length = 0, "—", before)}"" → ""{If(after.Length = 0, "—", after)}""")
        End If
    End Sub

    Public Shared Sub Delete(id As Integer)
        Using conn = Db.Open()
            Using tx = conn.BeginTransaction()
                Dim existing = SupplierRepository.GetById(conn, tx, id)
                If existing Is Nothing Then Throw New BusinessRuleException("This supplier no longer exists.")
                Dim inUse = SupplierRepository.CountItems(conn, tx, id)
                If inUse > 0 Then
                    Throw New BusinessRuleException(
                        $"""{existing.Name}"" is still the supplier of {Fmt.Plural(inUse, "item")}. " &
                        "Assign those items to another supplier (or remove them) before deleting this supplier.")
                End If
                SupplierRepository.Delete(conn, tx, id)
                ActivityRepository.Log(conn, tx, existing.Name, ActivityTypes.SupplierRemoved, "Removed supplier")
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
    End Sub
End Class
