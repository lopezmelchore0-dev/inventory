Imports Xunit

''' <summary>
''' The full account flow against a throwaway database (pawstock_auth_test):
''' Registration → Login → Profile → Change Password → Logout → Forgot Password → Login.
''' </summary>
<Collection("Database")>
Public Class AuthIntegrationTests
    Implements IDisposable

    Private Const TestDatabase As String = "pawstock_auth_test"
    Private Const Password1 As String = "Grooming#2026"
    Private Const Password2 As String = "Shampoo!Bath7"
    Private Const Password3 As String = "Brush&Comb42"

    Public Sub New()
        PasswordHasher.Iterations = 10000
        RememberedLogin.FilePath = IO.Path.Combine(IO.Path.GetTempPath(), "pawstock-tests", "login.json")
        AuthSession.SignOut()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        AuthSession.SignOut()
        DatabaseIntegrationTests.DropDatabase(TestDatabase)
    End Sub

    Private Shared Function Registration(fullName As String, username As String, password As String) As RegistrationInput
        Return New RegistrationInput With {
            .FullName = fullName, .Username = username, .Password = password, .ConfirmPassword = password,
            .SecurityQuestion = SecurityQuestions.All(0), .SecurityAnswer = "Bantay"
        }
    End Function

    Private Shared Function LoginError(username As String, password As String) As String
        Return Assert.Throws(Of BusinessRuleException)(Sub() AuthService.Login(username, password)).Message
    End Function

    <IntegrationFact>
    Public Sub FullAccountFlow()
        DatabaseIntegrationTests.ResetDatabase(TestDatabase)
        Assert.False(AuthService.HasAnyAdmin())

        ' ---- Registration ----
        Dim invalid = Assert.Throws(Of InputValidationException)(
            Sub() AuthService.Register(New RegistrationInput With {.Password = "short", .ConfirmPassword = "other"}))
        For Each key In {"fullname", "username", "password", "confirm", "question", "answer"}
            Assert.True(invalid.Errors.ContainsKey(key), key)
        Next

        Dim admin = AuthService.Register(Registration("Maria Santos", "maria", Password1))
        Assert.Equal(AdminAccount.AdminRole, admin.Role)
        Assert.True(AuthService.HasAnyAdmin())
        Assert.False(AuthSession.IsAuthenticated)

        Using conn = Db.Open()
            Dim stored = Convert.ToString(Db.Scalar(conn, Nothing, "SELECT CONCAT(password_hash, security_answer_hash) FROM admin_accounts"))
            Assert.DoesNotContain(Password1, stored)
            Assert.DoesNotContain("Bantay", stored, StringComparison.OrdinalIgnoreCase)
            ' Only the Admin role can ever be stored.
            Assert.ThrowsAny(Of Exception)(Sub() Db.Execute(conn, Nothing, "UPDATE admin_accounts SET role = 'Staff'"))
        End Using

        ' Public registration is first-run only.
        Dim closed = Assert.Throws(Of BusinessRuleException)(Sub() AuthService.Register(Registration("Second", "second", Password1)))
        Assert.Equal(AuthService.RegistrationClosedMessage, closed.Message)

        ' ---- Login ----
        Assert.Equal(AuthService.InvalidCredentialsMessage, LoginError("maria", "Wrong#Pass1"))
        Assert.Equal(AuthService.InvalidCredentialsMessage, LoginError("nobody", Password1))
        Assert.Throws(Of InputValidationException)(Sub() AuthService.Login("", ""))
        Dim signedIn = AuthService.Login("MARIA", Password1)
        Assert.True(AuthSession.IsAuthenticated)
        Assert.Equal("maria", signedIn.Username)
        Assert.True(signedIn.LastLoginAt.HasValue)

        ' Signed-in Admins can add more Admins; duplicate usernames are rejected regardless of case.
        Dim duplicate = Assert.Throws(Of InputValidationException)(
            Sub() AuthService.Register(Registration("Copy", "Maria", Password1), addedByAdmin:=True))
        Assert.Equal("This username is already taken.", duplicate.Errors("username"))
        AuthService.Register(Registration("Jose Reyes", "jose", Password2), addedByAdmin:=True)

        ' Activities record who did them.
        SupplierService.Add(New Supplier With {.Name = "Aparri Pet Trading", .ContactPerson = "Ana", .Phone = "09171234567", .Address = "Aparri"})
        Assert.Equal("maria", ActivityService.GetRecent(1).Single().PerformedBy)

        ' ---- Profile ----
        Dim wrongPassword = Assert.Throws(Of InputValidationException)(
            Sub() AuthService.UpdateProfile(New ProfileInput With {
                .FullName = "Maria S.", .Username = "maria", .SecurityQuestion = SecurityQuestions.All(0), .CurrentPassword = "Nope#1234"}))
        Assert.Equal("Current password is incorrect.", wrongPassword.Errors("current"))
        Dim takenName = Assert.Throws(Of InputValidationException)(
            Sub() AuthService.UpdateProfile(New ProfileInput With {
                .FullName = "Maria", .Username = "JOSE", .SecurityQuestion = SecurityQuestions.All(0), .CurrentPassword = Password1}))
        Assert.True(takenName.Errors.ContainsKey("username"))
        Dim needsAnswer = Assert.Throws(Of InputValidationException)(
            Sub() AuthService.UpdateProfile(New ProfileInput With {
                .FullName = "Maria", .Username = "maria", .SecurityQuestion = SecurityQuestions.All(1), .CurrentPassword = Password1}))
        Assert.True(needsAnswer.Errors.ContainsKey("answer"))

        Assert.False(AuthService.UpdateProfile(New ProfileInput With {
            .FullName = "Maria Santos", .Username = "maria", .SecurityQuestion = SecurityQuestions.All(0), .CurrentPassword = Password1}))
        Assert.True(AuthService.UpdateProfile(New ProfileInput With {
            .FullName = "Maria Dela Cruz", .Username = "maria.dc", .SecurityQuestion = SecurityQuestions.All(2),
            .NewSecurityAnswer = "  San  Juan ", .CurrentPassword = Password1}))
        Assert.Equal("Maria Dela Cruz", AuthSession.Current.FullName)
        Assert.Equal("maria.dc", AuthService.GetCurrentAccount().Username)
        Assert.Equal(SecurityQuestions.All(2), AuthSession.Current.SecurityQuestion)

        ' ---- Change password ----
        Dim same = Assert.Throws(Of InputValidationException)(Sub() AuthService.ChangePassword(Password1, Password1, Password1))
        Assert.True(same.Errors.ContainsKey("password"))
        Dim mismatch = Assert.Throws(Of InputValidationException)(Sub() AuthService.ChangePassword(Password1, Password2, Password3))
        Assert.True(mismatch.Errors.ContainsKey("confirm"))
        Dim weak = Assert.Throws(Of InputValidationException)(Sub() AuthService.ChangePassword(Password1, "weakpass", "weakpass"))
        Assert.True(weak.Errors.ContainsKey("password"))
        Dim wrongCurrent = Assert.Throws(Of InputValidationException)(Sub() AuthService.ChangePassword("Nope#1234", Password2, Password2))
        Assert.True(wrongCurrent.Errors.ContainsKey("current"))
        AuthService.ChangePassword(Password1, Password2, Password2)

        ' ---- Logout ----
        AuthService.Logout()
        Assert.False(AuthSession.IsAuthenticated)
        Assert.Throws(Of NotAuthenticatedException)(Sub() AuthService.GetCurrentAccount())
        Assert.Throws(Of NotAuthenticatedException)(Sub() AuthService.ChangePassword(Password2, Password3, Password3))
        Assert.Equal(AuthService.InvalidCredentialsMessage, LoginError("maria.dc", Password1))
        AuthService.Login("maria.dc", Password2)
        AuthService.Logout()

        ' ---- Forgot password ----
        Assert.Equal(SecurityQuestions.All(2), AuthService.BeginRecovery("maria.dc"))
        ' Unknown usernames get a stand-in question (same every time) and the same generic failure.
        Dim decoy = AuthService.BeginRecovery("ghost")
        Assert.Contains(decoy, SecurityQuestions.All)
        Assert.Equal(decoy, AuthService.BeginRecovery("GHOST"))
        Assert.Equal(AuthService.NotVerifiedMessage,
                     Assert.Throws(Of BusinessRuleException)(Sub() AuthService.VerifyRecoveryAnswer("ghost", "anything")).Message)
        Assert.Equal(AuthService.NotVerifiedMessage,
                     Assert.Throws(Of BusinessRuleException)(Sub() AuthService.VerifyRecoveryAnswer("maria.dc", "Bantay")).Message)

        ' The answer is not case- or space-sensitive, and the new password must differ from the previous one.
        Dim ticket = AuthService.VerifyRecoveryAnswer("maria.dc", "san juan")
        Dim reused = Assert.Throws(Of InputValidationException)(Sub() AuthService.ResetPassword(ticket, Password2, Password2))
        Assert.Contains("different", reused.Errors("password"))
        Assert.Throws(Of InputValidationException)(Sub() AuthService.ResetPassword(ticket, Password3, Password1))
        AuthService.ResetPassword(ticket, Password3, Password3)
        Assert.Throws(Of BusinessRuleException)(Sub() AuthService.ResetPassword(ticket, "Another#Pass9", "Another#Pass9"))

        ' ---- Login with the new password ----
        Assert.Equal(AuthService.InvalidCredentialsMessage, LoginError("maria.dc", Password2))
        AuthService.Login("maria.dc", Password3)
        Assert.True(AuthSession.IsAuthenticated)

        Dim types = ActivityService.GetRecent(50).Select(Function(a) a.ActivityType).ToList()
        For Each expected In {ActivityTypes.AdminAccountCreated, ActivityTypes.AdminProfileUpdated, ActivityTypes.PasswordChanged, ActivityTypes.PasswordReset}
            Assert.Contains(expected, types)
        Next
        Assert.DoesNotContain(ActivityService.GetRecent(50), Function(a) a.Description.Contains("San Juan", StringComparison.OrdinalIgnoreCase))
    End Sub

    <IntegrationFact>
    Public Sub RepeatedFailures_LockLoginAndRecovery()
        DatabaseIntegrationTests.ResetDatabase(TestDatabase)
        AuthService.Register(Registration("Maria Santos", "maria", Password1))

        For i = 1 To AuthThrottleRepository.MaxFailures
            Assert.Equal(AuthService.InvalidCredentialsMessage, LoginError("maria", "Wrong#Pass" & i))
        Next
        ' Locked: even the right password is refused for now.
        Assert.Contains("Too many failed attempts", LoginError("maria", Password1))
        ' Unknown usernames lock the same way, so a lock reveals nothing.
        For i = 1 To AuthThrottleRepository.MaxFailures
            LoginError("ghost", "Wrong#Pass" & i)
        Next
        Assert.Contains("Too many failed attempts", LoginError("ghost", "Wrong#Pass"))

        For i = 1 To AuthThrottleRepository.MaxFailures
            Assert.Throws(Of BusinessRuleException)(Sub() AuthService.VerifyRecoveryAnswer("maria", "wrong"))
        Next
        Dim locked = Assert.Throws(Of BusinessRuleException)(Sub() AuthService.VerifyRecoveryAnswer("maria", "Bantay"))
        Assert.Contains("Too many failed attempts", locked.Message)
        Assert.Contains("Too many failed attempts", Assert.Throws(Of BusinessRuleException)(Sub() AuthService.BeginRecovery("maria")).Message)

        ' Once the lock expires, the correct answer works again and a successful reset clears the login lock too.
        Using conn = Db.Open()
            Db.Execute(conn, Nothing, "UPDATE auth_throttle SET locked_until = NOW() - INTERVAL 1 SECOND")
        End Using
        Dim ticket = AuthService.VerifyRecoveryAnswer("maria", "bantay")
        AuthService.ResetPassword(ticket, Password2, Password2)
        AuthService.Login("maria", Password2)
        Assert.True(AuthSession.IsAuthenticated)
    End Sub

    <IntegrationFact>
    Public Sub BackupAndRestore_KeepAdminAccounts()
        DatabaseIntegrationTests.ResetDatabase(TestDatabase)
        AuthService.Register(Registration("Maria Santos", "maria", Password1))
        AuthService.Login("maria", Password1)

        Dim path = IO.Path.Combine(IO.Path.GetTempPath(), "pawstock-tests", "auth-backup.json")
        BackupService.CreateBackup(path)
        Dim backupText = IO.File.ReadAllText(path)
        Assert.DoesNotContain("password_hash", backupText)
        Assert.DoesNotContain("PBKDF2", backupText)

        AuthService.Register(Registration("Jose Reyes", "jose", Password2), addedByAdmin:=True)
        BackupService.Restore(BackupService.ReadBackup(path))
        ' Accounts are not part of the backup, so a restore never removes or rolls back an Admin.
        AuthService.Logout()
        AuthService.Login("jose", Password2)
        AuthService.Login("maria", Password1)
    End Sub
End Class
