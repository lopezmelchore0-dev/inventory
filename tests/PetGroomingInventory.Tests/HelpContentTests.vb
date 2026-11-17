Imports Xunit

''' <summary>Help &amp; Support content: the preview topics, and the tips carried over from the old Help dialog.</summary>
Public Class HelpContentTests

    <Fact>
    Public Sub Topics_AreTheEightPreviewTopicsInOrder()
        Dim titles = HelpContent.Topics.Select(Function(t) t.Title).ToArray()
        Assert.Equal({"System Overview", "Getting Started", "Managing Inventory Items", "Adding New Items",
                      "Stock Adjustment", "Inventory Monitoring", "Generating Inventory Reports",
                      "Frequently Asked Questions"}, titles)
    End Sub

    <Fact>
    Public Sub EveryTopic_HasCardTextAndGuideBody()
        Assert.False(String.IsNullOrWhiteSpace(HelpContent.Intro))
        Assert.Equal(HelpContent.Topics.Count, HelpContent.Topics.Select(Function(t) t.Key).Distinct().Count())
        For Each topic In HelpContent.Topics
            Assert.False(String.IsNullOrWhiteSpace(topic.Icon), topic.Title)
            Assert.False(String.IsNullOrWhiteSpace(topic.Summary), topic.Title)
            Assert.False(String.IsNullOrWhiteSpace(topic.Intro), topic.Title)
            Assert.True(topic.HasBullets OrElse topic.HasFaqs, topic.Title)
        Next
    End Sub

    <Fact>
    Public Sub Faqs_HaveQuestionsAndAnswers()
        Dim faqs = HelpContent.Topics.Single(Function(t) t.Key = "faqs")
        Assert.True(faqs.HasFaqs)
        Assert.All(faqs.Faqs, Sub(f)
                                  Assert.False(String.IsNullOrWhiteSpace(f.Question))
                                  Assert.False(String.IsNullOrWhiteSpace(f.Answer))
                              End Sub)
    End Sub

    <Theory>
    <InlineData("Inventory → Add Item to register a product")>
    <InlineData("Expiration date is optional")>
    <InlineData("Record deliveries with Stock In")>
    <InlineData("Stock Adjustment (a reason is required)")>
    <InlineData("Green = In Stock, Yellow = Low Stock, Red = Critical Stock, Gray = Out of Stock")>
    <InlineData("Stock Monitoring lists items that need restocking and items that are expired or expiring soon")>
    <InlineData("Print it or export it to PDF or Excel")>
    <InlineData("Settings → Backup & Restore")>
    <InlineData("add another Admin")>
    <InlineData("""Forgot Password?"" on the Login screen")>
    <InlineData("start MySQL in the XAMPP Control Panel")>
    Public Sub Guide_KeepsEveryTipFromTheOldHelpDialog(tip As String)
        Dim allText = String.Join(vbLf, HelpContent.Topics.Select(Function(t) t.AllText()))
        Assert.Contains(tip, allText)
    End Sub

    <Fact>
    Public Sub ReportsTopic_ListsEveryReportType()
        Dim reports = HelpContent.Topics.Single(Function(t) t.Key = "reports").AllText()
        For Each type In ReportTypes.All
            Assert.Contains(ReportTypes.Title(type), reports)
        Next
    End Sub
End Class
