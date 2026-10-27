Imports Xunit

''' <summary>Password hashing, password rules and security-question helpers (no database needed).</summary>
Public Class AuthUnitTests

    Public Sub New()
        PasswordHasher.Iterations = 10000
    End Sub

    <Fact>
    Public Sub Hash_RoundTripsAndUsesUniqueSalts()
        Dim first = PasswordHasher.Hash("Grooming#2026")
        Dim second = PasswordHasher.Hash("Grooming#2026")
        Assert.NotEqual(first, second)
        Assert.StartsWith(PasswordHasher.Scheme & "$", first)
        Assert.DoesNotContain("Grooming", first)
        Assert.True(PasswordHasher.Verify("Grooming#2026", first))
        Assert.True(PasswordHasher.Verify("Grooming#2026", second))
        Assert.False(PasswordHasher.Verify("grooming#2026", first))
        Assert.False(PasswordHasher.Verify("", first))
    End Sub

    <Fact>
    Public Sub Verify_RejectsMalformedOrTamperedHashes()
        Dim stored = PasswordHasher.Hash("Grooming#2026")
        Dim parts = stored.Split("$"c)
        Dim tamperedHash = Convert.FromBase64String(parts(3))
        tamperedHash(0) = tamperedHash(0) Xor CByte(1)
        Dim tampered = String.Join("$", parts(0), parts(1), parts(2), Convert.ToBase64String(tamperedHash))

        Assert.False(PasswordHasher.Verify("Grooming#2026", tampered))
        Assert.False(PasswordHasher.Verify("Grooming#2026", Nothing))
        Assert.False(PasswordHasher.Verify("Grooming#2026", "Grooming#2026"))
        Assert.False(PasswordHasher.Verify("Grooming#2026", "PBKDF2-SHA256$abc$xx$yy"))
        Assert.False(PasswordHasher.Verify("Grooming#2026", "PBKDF2-SHA256$1$" & parts(2) & "$" & parts(3)))
    End Sub

    <Fact>
    Public Sub DefaultIterations_MeetCurrentGuidance()
        Assert.True(PasswordHasher.DefaultIterations >= 600000)
    End Sub

    <Theory>
    <InlineData("", "Password is required.")>
    <InlineData("Ab1!", "8 to 128 characters")>
    <InlineData("abcdefg1!", "uppercase")>
    <InlineData("ABCDEFG1!", "lowercase")>
    <InlineData("Abcdefgh!", "number")>
    <InlineData("Abcdefgh1", "symbol")>
    Public Sub Check_ReportsMissingRules(password As String, expected As String)
        Dim message = PasswordPolicy.Check(password)
        Assert.NotNull(message)
        Assert.Contains(expected, message, StringComparison.OrdinalIgnoreCase)
    End Sub

    <Fact>
    Public Sub Check_AcceptsStrongPasswordAndRejectsUsernameInside()
        Assert.Null(PasswordPolicy.Check("Grooming#2026", "maria"))
        Assert.Equal("Password must not contain your username.", PasswordPolicy.Check("Maria#2026x", "maria"))
        Assert.NotNull(PasswordPolicy.Check(New String("a"c, 120) & "A1!" & New String("b"c, 10)))
    End Sub

    <Fact>
    Public Sub Score_GrowsWithStrength()
        Assert.Equal(0, PasswordPolicy.Score(""))
        Assert.Equal(1, PasswordPolicy.Score("abc"))
        Assert.Equal(2, PasswordPolicy.Score("abcdefg1"))
        Assert.Equal(3, PasswordPolicy.Score("Groom#26"))
        Assert.Equal(4, PasswordPolicy.Score("Grooming#2026!"))
        Assert.Equal("Strong", PasswordPolicy.ScoreLabel(4))
    End Sub

    <Theory>
    <InlineData("", False)>
    <InlineData("abc", False)>
    <InlineData("maria santos", False)>
    <InlineData("maria-santos", False)>
    <InlineData("maria.santos_1", True)>
    <InlineData("Admin", True)>
    Public Sub CheckUsername_EnforcesFormat(username As String, valid As Boolean)
        Assert.Equal(valid, PasswordPolicy.CheckUsername(username) Is Nothing)
    End Sub

    <Fact>
    Public Sub AccountFieldChecks()
        Assert.NotNull(PasswordPolicy.CheckFullName(" "))
        Assert.Null(PasswordPolicy.CheckFullName("Maria Santos"))
        Assert.NotNull(PasswordPolicy.CheckQuestion(Nothing))
        Assert.NotNull(PasswordPolicy.CheckQuestion("What is my custom question?"))
        Assert.Null(PasswordPolicy.CheckQuestion(SecurityQuestions.All(0)))
        Assert.NotNull(PasswordPolicy.CheckAnswer("   "))
        Assert.Null(PasswordPolicy.CheckAnswer("Bantay"))
    End Sub

    <Fact>
    Public Sub NormalizeAnswer_IgnoresCaseAndExtraSpaces()
        Assert.Equal("san juan", PasswordHasher.NormalizeAnswer("  San   JUAN "))
        Dim stored = PasswordHasher.Hash(PasswordHasher.NormalizeAnswer("Bantay"))
        Assert.True(PasswordHasher.Verify(PasswordHasher.NormalizeAnswer(" bantay "), stored))
    End Sub

    <Fact>
    Public Sub DecoyQuestion_IsStableAndFromTheList()
        Dim a = SecurityQuestions.DecoyFor("ghost_user", "secret-1")
        Assert.Equal(a, SecurityQuestions.DecoyFor("  GHOST_user ", "secret-1"))
        Assert.Contains(a, SecurityQuestions.All)
        ' Different usernames spread over the list.
        Dim picks = Enumerable.Range(1, 200).Select(Function(i) SecurityQuestions.DecoyFor("user" & i, "secret-1")).Distinct().Count()
        Assert.True(picks >= SecurityQuestions.All.Count - 1)
    End Sub
End Class
