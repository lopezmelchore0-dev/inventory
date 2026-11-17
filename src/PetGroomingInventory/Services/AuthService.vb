Imports System.Collections.Concurrent
Imports System.Security.Cryptography
Imports MySqlConnector

Public Class RegistrationInput
    Public Property FullName As String = ""
    Public Property Username As String = ""
    Public Property Password As String = ""
    Public Property ConfirmPassword As String = ""
    Public Property SecurityQuestion As String = ""
    Public Property SecurityAnswer As String = ""
End Class

Public Class ProfileInput
    Public Property FullName As String = ""
    Public Property Username As String = ""
    Public Property SecurityQuestion As String = ""
    ''' <summary>Blank keeps the current answer (required when the question changes).</summary>
    Public Property NewSecurityAnswer As String = ""
    Public Property CurrentPassword As String = ""
End Class

''' <summary>
''' Admin accounts: registration, sign-in, password recovery by security question, password change and profile updates.
''' Failure messages are deliberately generic so they never reveal whether a username exists.
''' </summary>
Public NotInheritable Class AuthService
    Private Sub New()
    End Sub

    Public Const InvalidCredentialsMessage As String = "Invalid username or password."
    Public Const NotVerifiedMessage As String = "The information provided could not be verified."
    Public Const RegistrationClosedMessage As String =
        "An Admin account already exists. Please log in with that account."
    Private Const RecoveryTokenMinutes As Integer = 10

    Private Class RecoveryTicket
        Public Property AccountId As Integer
        Public Property Username As String
        Public Property ExpiresAt As Date
    End Class

    Private Shared ReadOnly _recoveryTickets As New ConcurrentDictionary(Of String, RecoveryTicket)

    Private Shared Function LockedMessage(minutes As Integer) As String
        Return $"Too many failed attempts. Please wait {Fmt.Plural(minutes, "minute")} and try again."
    End Function

    Public Shared Function HasAnyAdmin() As Boolean
        Using conn = Db.Open()
            Return AdminRepository.CountAll(conn, Nothing) > 0
        End Using
    End Function

    ' ================= Registration =================

    ''' <summary>Keys: fullname, username, password, confirm, question, answer.</summary>
    Public Shared Function ValidateRegistration(input As RegistrationInput) As Dictionary(Of String, String)
        Dim errors As New Dictionary(Of String, String)
        AddIf(errors, "fullname", PasswordPolicy.CheckFullName(input.FullName))
        AddIf(errors, "username", PasswordPolicy.CheckUsername(input.Username))
        AddIf(errors, "password", PasswordPolicy.Check(input.Password, input.Username))
        If String.IsNullOrEmpty(input.ConfirmPassword) Then
            errors("confirm") = "Please confirm your password."
        ElseIf input.ConfirmPassword <> input.Password Then
            errors("confirm") = "Passwords do not match."
        End If
        AddIf(errors, "question", PasswordPolicy.CheckQuestion(input.SecurityQuestion))
        AddIf(errors, "answer", PasswordPolicy.CheckAnswer(input.SecurityAnswer))
        Return errors
    End Function

    ''' <summary>
    ''' Creates an Admin account. Public registration (from the Login screen) is only allowed while no account exists;
    ''' a signed-in Admin can add further Admins (addedByAdmin = True).
    ''' </summary>
    Public Shared Function Register(input As RegistrationInput, Optional addedByAdmin As Boolean = False) As AdminAccount
        If addedByAdmin Then AuthSession.Require()
        Dim errors = ValidateRegistration(input)
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Dim passwordHash = PasswordHasher.Hash(input.Password)
        Dim answerHash = PasswordHasher.Hash(PasswordHasher.NormalizeAnswer(input.SecurityAnswer))
        Dim username = input.Username.Trim()
        Dim created As AdminAccount
        Try
            Using conn = Db.Open()
                Using tx = conn.BeginTransaction()
                    If Not addedByAdmin AndAlso AdminRepository.CountAll(conn, tx) > 0 Then
                        Throw New BusinessRuleException(RegistrationClosedMessage)
                    End If
                    If AdminRepository.UsernameExists(conn, tx, username, 0) Then
                        Throw New InputValidationException("username", "This username is already taken.")
                    End If
                    Dim id = AdminRepository.Insert(conn, tx, input.FullName, username, passwordHash, input.SecurityQuestion, answerHash)
                    created = AdminRepository.GetById(conn, tx, id).Account
                    ActivityRepository.Log(conn, tx, created.FullName, ActivityTypes.AdminAccountCreated,
                        If(addedByAdmin, $"Admin account ""{username}"" created by {AuthSession.Username}", $"Admin account ""{username}"" created"),
                        performedBy:=If(AuthSession.Username, username))
                    tx.Commit()
                End Using
            End Using
        Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
            Throw New InputValidationException("username", "This username is already taken.")
        End Try
        AppEvents.RaiseDataChanged()
        Return created
    End Function

    ' ================= Login / logout =================

    ''' <summary>Signs the Admin in. Throws BusinessRuleException with a generic message on any failure.</summary>
    Public Shared Function Login(username As String, password As String) As AdminAccount
        Dim errors As New Dictionary(Of String, String)
        If String.IsNullOrWhiteSpace(username) Then errors("username") = "Enter your username."
        If String.IsNullOrEmpty(password) Then errors("password") = "Enter your password."
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Dim key = AuthThrottleRepository.LoginKey(username)
        Using conn = Db.Open()
            Dim minutes = AuthThrottleRepository.LockedMinutesLeft(conn, Nothing, key)
            If minutes.HasValue Then Throw New BusinessRuleException(LockedMessage(minutes.Value))

            Dim creds = AdminRepository.GetByUsername(conn, Nothing, username)
            Dim ok As Boolean
            If creds Is Nothing Then
                PasswordHasher.VerifyDummy(password)
            Else
                ok = PasswordHasher.Verify(password, creds.PasswordHash)
            End If
            If Not ok Then
                AuthThrottleRepository.RegisterFailure(conn, Nothing, key)
                Throw New BusinessRuleException(InvalidCredentialsMessage)
            End If

            AuthThrottleRepository.Clear(conn, Nothing, key)
            AdminRepository.TouchLastLogin(conn, Nothing, creds.Account.Id)
            Dim account = AdminRepository.GetById(conn, Nothing, creds.Account.Id).Account
            AuthSession.SignIn(account)
            Return account
        End Using
    End Function

    Public Shared Sub Logout()
        AuthSession.SignOut()
    End Sub

    ' ================= Forgot password =================

    ''' <summary>
    ''' Step 1 → 2: returns the security question to ask. For a username that does not exist a stand-in
    ''' question is returned, so this step looks the same either way.
    ''' </summary>
    Public Shared Function BeginRecovery(username As String) As String
        If String.IsNullOrWhiteSpace(username) Then Throw New InputValidationException("username", "Enter your username.")
        Using conn = Db.Open()
            Dim minutes = AuthThrottleRepository.LockedMinutesLeft(conn, Nothing, AuthThrottleRepository.RecoveryKey(username))
            If minutes.HasValue Then Throw New BusinessRuleException(LockedMessage(minutes.Value))
            Dim creds = AdminRepository.GetByUsername(conn, Nothing, username)
            If creds IsNot Nothing Then Return creds.Account.SecurityQuestion
            Return SecurityQuestions.DecoyFor(username, AdminRepository.GetDecoyKey(conn, Nothing))
        End Using
    End Function

    ''' <summary>
    ''' Step 2 → 3: checks the security answer and returns a short-lived recovery ticket for ResetPassword.
    ''' Every failure gives the same message and counts toward the recovery lockout.
    ''' </summary>
    Public Shared Function VerifyRecoveryAnswer(username As String, answer As String) As String
        If String.IsNullOrWhiteSpace(username) Then Throw New BusinessRuleException(NotVerifiedMessage)
        If String.IsNullOrWhiteSpace(answer) Then Throw New InputValidationException("answer", "Enter your security answer.")

        Dim key = AuthThrottleRepository.RecoveryKey(username)
        Using conn = Db.Open()
            Dim minutes = AuthThrottleRepository.LockedMinutesLeft(conn, Nothing, key)
            If minutes.HasValue Then Throw New BusinessRuleException(LockedMessage(minutes.Value))

            Dim creds = AdminRepository.GetByUsername(conn, Nothing, username)
            Dim normalized = PasswordHasher.NormalizeAnswer(answer)
            Dim ok As Boolean
            If creds Is Nothing Then
                PasswordHasher.VerifyDummy(normalized)
            Else
                ok = PasswordHasher.Verify(normalized, creds.AnswerHash)
            End If
            If Not ok Then
                AuthThrottleRepository.RegisterFailure(conn, Nothing, key)
                Throw New BusinessRuleException(NotVerifiedMessage)
            End If

            PurgeExpiredTickets()
            Dim ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            _recoveryTickets(ticket) = New RecoveryTicket With {
                .AccountId = creds.Account.Id, .Username = creds.Account.Username, .ExpiresAt = Date.UtcNow.AddMinutes(RecoveryTokenMinutes)
            }
            Return ticket
        End Using
    End Function

    ''' <summary>Step 3: sets the new password. Keys: password, confirm.</summary>
    Public Shared Sub ResetPassword(ticket As String, newPassword As String, confirmPassword As String)
        Dim info As RecoveryTicket = Nothing
        If String.IsNullOrEmpty(ticket) OrElse Not _recoveryTickets.TryGetValue(ticket, info) OrElse info.ExpiresAt < Date.UtcNow Then
            If Not String.IsNullOrEmpty(ticket) Then _recoveryTickets.TryRemove(ticket, Nothing)
            Throw New BusinessRuleException("This password reset has expired. Please start again.")
        End If

        Using conn = Db.Open()
            Dim creds = AdminRepository.GetById(conn, Nothing, info.AccountId)
            If creds Is Nothing Then
                _recoveryTickets.TryRemove(ticket, Nothing)
                Throw New BusinessRuleException(NotVerifiedMessage)
            End If
            Dim errors = ValidateNewPassword(newPassword, confirmPassword, creds.Account.Username)
            If Not errors.ContainsKey("password") AndAlso PasswordHasher.Verify(newPassword, creds.PasswordHash) Then
                errors("password") = "Your new password must be different from your previous password."
            End If
            If errors.Count > 0 Then Throw New InputValidationException(errors)

            Dim hash = PasswordHasher.Hash(newPassword)
            Using tx = conn.BeginTransaction()
                AdminRepository.UpdatePassword(conn, tx, creds.Account.Id, hash)
                AuthThrottleRepository.Clear(conn, tx, AuthThrottleRepository.RecoveryKey(creds.Account.Username))
                AuthThrottleRepository.Clear(conn, tx, AuthThrottleRepository.LoginKey(creds.Account.Username))
                ActivityRepository.Log(conn, tx, creds.Account.FullName, ActivityTypes.PasswordReset,
                                       "Password reset with the security question", performedBy:=creds.Account.Username)
                tx.Commit()
            End Using
        End Using
        _recoveryTickets.TryRemove(ticket, Nothing)
        AppEvents.RaiseDataChanged()
    End Sub

    Private Shared Sub PurgeExpiredTickets()
        For Each kv In _recoveryTickets.Where(Function(t) t.Value.ExpiresAt < Date.UtcNow).ToList()
            _recoveryTickets.TryRemove(kv.Key, Nothing)
        Next
    End Sub

    ' ================= Change password =================

    ''' <summary>Keys: current, password, confirm.</summary>
    Public Shared Sub ChangePassword(currentPassword As String, newPassword As String, confirmPassword As String)
        Dim session = AuthSession.Require()
        Dim errors As New Dictionary(Of String, String)
        If String.IsNullOrEmpty(currentPassword) Then errors("current") = "Enter your current password."
        For Each kv In ValidateNewPassword(newPassword, confirmPassword, session.Username)
            errors(kv.Key) = kv.Value
        Next
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Using conn = Db.Open()
            Dim creds = RequireCurrentPassword(conn, session, currentPassword)
            If newPassword = currentPassword OrElse PasswordHasher.Verify(newPassword, creds.PasswordHash) Then
                Throw New InputValidationException("password", "Your new password must be different from your current password.")
            End If
            Dim hash = PasswordHasher.Hash(newPassword)
            Using tx = conn.BeginTransaction()
                AdminRepository.UpdatePassword(conn, tx, session.Id, hash)
                ActivityRepository.Log(conn, tx, session.FullName, ActivityTypes.PasswordChanged, "Password changed from Account Settings")
                tx.Commit()
            End Using
        End Using
        AppEvents.RaiseDataChanged()
    End Sub

    Private Shared Function ValidateNewPassword(newPassword As String, confirmPassword As String, username As String) As Dictionary(Of String, String)
        Dim errors As New Dictionary(Of String, String)
        If String.IsNullOrEmpty(newPassword) Then
            errors("password") = "New password is required."
        Else
            AddIf(errors, "password", PasswordPolicy.Check(newPassword, username))
        End If
        If String.IsNullOrEmpty(confirmPassword) Then
            errors("confirm") = "Please confirm your new password."
        ElseIf confirmPassword <> newPassword Then
            errors("confirm") = "Passwords do not match."
        End If
        Return errors
    End Function

    ''' <summary>Verifies the signed-in Admin's password (throttled like sign-in). Throws a field error keyed "current".</summary>
    Private Shared Function RequireCurrentPassword(conn As MySqlConnection, session As AdminAccount, currentPassword As String) As AdminCredentials
        Dim key = AuthThrottleRepository.LoginKey(session.Username)
        Dim minutes = AuthThrottleRepository.LockedMinutesLeft(conn, Nothing, key)
        If minutes.HasValue Then Throw New BusinessRuleException(LockedMessage(minutes.Value))
        Dim creds = AdminRepository.GetById(conn, Nothing, session.Id)
        If creds Is Nothing Then
            AuthSession.SignOut()
            Throw New NotAuthenticatedException()
        End If
        If Not PasswordHasher.Verify(currentPassword, creds.PasswordHash) Then
            AuthThrottleRepository.RegisterFailure(conn, Nothing, key)
            Throw New InputValidationException("current", "Current password is incorrect.")
        End If
        AuthThrottleRepository.Clear(conn, Nothing, key)
        Return creds
    End Function

    ' ================= Profile =================

    Public Shared Function GetCurrentAccount() As AdminAccount
        Dim session = AuthSession.Require()
        Using conn = Db.Open()
            Dim creds = AdminRepository.GetById(conn, Nothing, session.Id)
            If creds Is Nothing Then
                AuthSession.SignOut()
                Throw New NotAuthenticatedException()
            End If
            AuthSession.Refresh(creds.Account)
            Return creds.Account
        End Using
    End Function

    ''' <summary>Keys: fullname, username, question, answer, current. Returns False when nothing changed.</summary>
    Public Shared Function UpdateProfile(input As ProfileInput) As Boolean
        Dim session = AuthSession.Require()
        Dim errors As New Dictionary(Of String, String)
        AddIf(errors, "fullname", PasswordPolicy.CheckFullName(input.FullName))
        AddIf(errors, "username", PasswordPolicy.CheckUsername(input.Username))
        AddIf(errors, "question", PasswordPolicy.CheckQuestion(input.SecurityQuestion))
        Dim newAnswer = If(input.NewSecurityAnswer, "")
        If newAnswer.Trim().Length > 0 Then
            AddIf(errors, "answer", PasswordPolicy.CheckAnswer(newAnswer))
        ElseIf input.SecurityQuestion <> session.SecurityQuestion AndAlso Not errors.ContainsKey("question") Then
            errors("answer") = "Enter the answer to your new security question."
        End If
        If String.IsNullOrEmpty(input.CurrentPassword) Then errors("current") = "Enter your current password to save changes."
        If errors.Count > 0 Then Throw New InputValidationException(errors)

        Dim fullName = input.FullName.Trim()
        Dim username = input.Username.Trim()
        Dim changes As New List(Of String)
        If fullName <> session.FullName Then changes.Add($"full name ""{session.FullName}"" → ""{fullName}""")
        If username <> session.Username Then changes.Add($"username ""{session.Username}"" → ""{username}""")
        If input.SecurityQuestion <> session.SecurityQuestion Then changes.Add("security question")
        If newAnswer.Trim().Length > 0 Then changes.Add("security answer")

        Try
            Using conn = Db.Open()
                RequireCurrentPassword(conn, session, input.CurrentPassword)
                If changes.Count = 0 Then Return False
                Dim answerHash = If(newAnswer.Trim().Length > 0, PasswordHasher.Hash(PasswordHasher.NormalizeAnswer(newAnswer)), Nothing)
                Using tx = conn.BeginTransaction()
                    If AdminRepository.UsernameExists(conn, tx, username, session.Id) Then
                        Throw New InputValidationException("username", "This username is already taken.")
                    End If
                    AdminRepository.UpdateProfile(conn, tx, session.Id, fullName, username, input.SecurityQuestion, answerHash)
                    ActivityRepository.Log(conn, tx, fullName, ActivityTypes.AdminProfileUpdated, "Changed " & String.Join("; ", changes),
                                           performedBy:=username)
                    tx.Commit()
                End Using
                AuthSession.Refresh(AdminRepository.GetById(conn, Nothing, session.Id).Account)
            End Using
        Catch ex As MySqlException When ex.ErrorCode = MySqlErrorCode.DuplicateKeyEntry
            Throw New InputValidationException("username", "This username is already taken.")
        End Try
        AppEvents.RaiseDataChanged()
        Return True
    End Function

    Private Shared Sub AddIf(errors As Dictionary(Of String, String), key As String, message As String)
        If message IsNot Nothing Then errors(key) = message
    End Sub
End Class
