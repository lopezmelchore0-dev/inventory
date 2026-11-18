''' <summary>One question and answer in the Help &amp; Support FAQs.</summary>
Public NotInheritable Class HelpFaq
    Public Sub New(question As String, answer As String)
        Me.Question = question
        Me.Answer = answer
    End Sub

    Public ReadOnly Property Question As String
    Public ReadOnly Property Answer As String
End Class

''' <summary>
''' One Help &amp; Support topic. The same object feeds the preview card (Icon, Title, Summary)
''' and the full guide section (Intro, Bullets, Faqs), so the two never drift apart.
''' </summary>
Public NotInheritable Class HelpTopic
    Public Sub New(key As String, icon As String, title As String, summary As String, intro As String,
                   bullets As IEnumerable(Of String), Optional faqs As IEnumerable(Of HelpFaq) = Nothing)
        Me.Key = key
        Me.Icon = icon
        Me.Title = title
        Me.Summary = summary
        Me.Intro = intro
        Me.Bullets = bullets.ToList().AsReadOnly()
        Me.Faqs = If(faqs, Enumerable.Empty(Of HelpFaq)()).ToList().AsReadOnly()
    End Sub

    Public ReadOnly Property Key As String
    ''' <summary>Segoe Fluent Icons glyph.</summary>
    Public ReadOnly Property Icon As String
    Public ReadOnly Property Title As String
    ''' <summary>One or two lines shown on the preview card.</summary>
    Public ReadOnly Property Summary As String
    Public ReadOnly Property Intro As String
    Public ReadOnly Property Bullets As IReadOnlyList(Of String)
    Public ReadOnly Property Faqs As IReadOnlyList(Of HelpFaq)

    Public ReadOnly Property HasBullets As Boolean
        Get
            Return Bullets.Count > 0
        End Get
    End Property

    Public ReadOnly Property HasFaqs As Boolean
        Get
            Return Faqs.Count > 0
        End Get
    End Property

    ''' <summary>All text in the topic, for searching and tests.</summary>
    Public Function AllText() As String
        Dim parts As New List(Of String) From {Title, Summary, Intro}
        parts.AddRange(Bullets)
        For Each faq In Faqs
            parts.Add(faq.Question)
            parts.Add(faq.Answer)
        Next
        Return String.Join(vbLf, parts)
    End Function
End Class

''' <summary>The Help &amp; Support documentation shown in the preview and the full guide.</summary>
Public NotInheritable Class HelpContent
    Private Sub New()
    End Sub

    Public Const Intro As String =
        "Find quick answers and step-by-step guides for running your shop's inventory in PawStock. " &
        "Pick a topic below to jump straight to it, or open the full guide to read everything in one place."

    Public Shared ReadOnly Property Topics As IReadOnlyList(Of HelpTopic) = BuildTopics().AsReadOnly()

    Private Shared Function BuildTopics() As List(Of HelpTopic)
        Dim reportNames = String.Join(", ", ReportTypes.All.Select(Function(t) ReportTypes.Title(t)))

        Return New List(Of HelpTopic) From {
            New HelpTopic("overview", ChrW(&HE946), "System Overview",
                "What PawStock does and how the main pages fit together.",
                "PawStock keeps track of the grooming products your shop sells and uses: how much is on hand, when it expires, who supplies it and every change made to it.",
                {
                    "Dashboard: your inventory at a glance, with stock alerts, items expiring soon and the latest activity.",
                    "Inventory: every product with its category, pet type, supplier, price, quantity and stock status.",
                    "Stock Monitoring: items that need restocking, expiration alerts, and the Stock-In and Adjustment history.",
                    "Suppliers: the companies and contacts you buy products from.",
                    "Activities: a complete history of inventory operations, with who did what and when.",
                    "Reports: printable and exportable inventory reports for any period.",
                    "Settings: stock status rules, categories and units, backups and sample data."
                }),
            New HelpTopic("getting-started", ChrW(&HE768), "Getting Started",
                "First steps after signing in: sample data, stock rules and the database.",
                "A few minutes of setup makes every stock status and alert match the way your shop works.",
                {
                    "Make sure MySQL is running in the XAMPP Control Panel before you open PawStock. The database and tables are created automatically.",
                    "Sign in with your Admin account. The first Admin is created on the Register screen the first time PawStock runs.",
                    "Want to explore first? Go to Settings → Sample Data and click Load Sample Data. Sample records are tagged SAMPLE and can be removed later with Remove Sample Data.",
                    "Set your Stock Status Rules in Settings: the Low stock and Critical thresholds, and how many days count as Expiring soon.",
                    "Add your categories in Settings, then add your suppliers on the Suppliers page so they are ready when you add items."
                }),
            New HelpTopic("managing-items", ChrW(&HE7B8), "Managing Inventory Items",
                "Search, filter, edit and remove the products in your inventory.",
                "The Inventory page lists every product with its quantity and stock status, and is where you keep item details up to date.",
                {
                    "Type in the search box to find items by name or description. Use the filters to narrow the list, and Clear filters to see everything again.",
                    "The search box at the top of every page searches the inventory too: type a name and press Enter.",
                    "Use the Actions column to edit an item's details or remove it.",
                    "To change an item's quantity, use Stock In or Stock Adjustment instead of editing it, so the change is recorded in the history.",
                    "Changing an item's unit does not convert its quantity."
                }),
            New HelpTopic("adding-items", ChrW(&HE710), "Adding New Items",
                "Register a new product with its category, supplier and price.",
                "Every product you want to track starts with Inventory → Add Item.",
                {
                    "Go to Inventory → Add Item to register a product.",
                    "Fill in the required fields: Item Name, Category, Pet Type, Supplier, Unit and Retail Price.",
                    "Pick the Unit the item is counted in: Pieces, Bottle, Gallon, Pack, Box, Set, Bundle, Bag, Sachet, Can, Roll, Pair, Tub, Pail, Jar or Tube. Choose Others to type your own.",
                    "Expiration date is optional. Leave it empty for products that don't expire.",
                    "Description is optional. Use it for size, scent or other details, e.g. 500 ml, Lavender.",
                    "Supplier not listed yet? Click New supplier in the form to add one without leaving it.",
                    "After saving, record the quantity you have on hand with Stock In."
                }),
            New HelpTopic("stock-adjustment", ChrW(&HE8AB), "Stock Adjustment",
                "Record deliveries with Stock In and correct count differences.",
                "There are two ways to change a quantity, and both are recorded with the date and the Admin who made the change.",
                {
                    "Record deliveries with Stock In: choose the item, enter the Quantity Received and Date Received, and the supplier when applicable.",
                    "Fix count differences with Stock Adjustment (a reason is required). Choose Add stock, Deduct stock or Set counted quantity.",
                    "Set counted quantity is useful after a physical count: enter the number you counted and PawStock works out the difference.",
                    "The Unit field is filled in with the unit chosen for the item in Add Item (e.g. Pieces, Bottle, Pack). Stock is always recorded in that unit; to use another one, change it in Inventory › Edit item first.",
                    "Add an optional note, such as a delivery receipt number, to make the history easier to follow.",
                    "Every Stock In and adjustment appears in Stock Monitoring's history tabs and on the Activities page."
                }),
            New HelpTopic("monitoring", ChrW(&HE9D9), "Inventory Monitoring",
                "Stock status colors, restock lists and expiration alerts.",
                "PawStock checks every item against your stock rules and expiration dates, and flags what needs attention.",
                {
                    "Stock status: Green = In Stock, Yellow = Low Stock, Red = Critical Stock, Gray = Out of Stock. Thresholds are set in Settings.",
                    "Stock Monitoring lists items that need restocking and items that are expired or expiring soon.",
                    "Use Restock or Adjust beside an item to fix it right from the list.",
                    "The bell at the top of the window shows how many alerts are waiting. Click it to open Stock Monitoring.",
                    "Statuses update automatically everywhere when your thresholds change."
                }),
            New HelpTopic("reports", ChrW(&HE9F9), "Generating Inventory Reports",
                "Build, print and export inventory reports for any period.",
                "Reports summarize your inventory for a period, ready to print or share.",
                {
                    "Reports: pick a report type, Inventory From and Inventory To, then Generate Report. Print it or export it to PDF or Excel.",
                    "Use a Quick range (Today, This Month, Last Month, This Year or All Time) to fill in the dates for you.",
                    "Available reports: " & reportNames & ".",
                    "Use Reset Filters to start over with the default report."
                }),
            New HelpTopic("faqs", ChrW(&HE897), "Frequently Asked Questions",
                "Quick answers about backups, your account and connection problems.",
                "Answers to the questions Admins ask most often.",
                Array.Empty(Of String)(),
                {
                    New HelpFaq("How do I keep my data safe?",
                        "Back up your data regularly in Settings → Backup & Restore. A backup saves all items, suppliers, categories, units, stock history, activity history and settings as a single .json file."),
                    New HelpFaq("What happens when I restore a backup?",
                        "Restoring replaces ALL current data with the backup. The file is checked first, and a safety copy of your current data is saved automatically before anything is replaced."),
                    New HelpFaq("How do I update my account?",
                        "My Profile: update your details and security question, or change your password."),
                    New HelpFaq("I forgot my password. What should I do?",
                        "Use ""Forgot Password?"" on the Login screen and answer your security question."),
                    New HelpFaq("PawStock says it can't connect to the database.",
                        "If the app can't connect, start MySQL in the XAMPP Control Panel, wait until it turns green, then click Retry."),
                    New HelpFaq("Why don't current stock figures change with the report period?",
                        "Current inventory figures are the stock on hand when the report was generated. PawStock does not keep past stock levels, so those figures are not limited to the selected period."),
                    New HelpFaq("Why can't I remove a category?",
                        "Categories that are still used by an item cannot be removed. Move those items to another category first.")
                })
        }
    End Function
End Class
