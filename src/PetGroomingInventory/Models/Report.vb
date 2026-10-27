Imports System.Globalization

Public Enum ReportType
    Summary
    InventoryList
    LowStock
    OutOfStock
    Expiring
    Category
    PetType
    Movement
End Enum

''' <summary>How a report value is formatted on screen, in print and PDF, and typed in Excel.</summary>
Public Enum ReportValueKind
    Text
    Count
    Quantity
    SignedQuantity
    Money
    [Date]
End Enum

''' <summary>Names and date rules for each report type.</summary>
Public NotInheritable Class ReportTypes
    Private Sub New()
    End Sub

    Public Shared ReadOnly All As ReportType() = CType([Enum].GetValues(GetType(ReportType)), ReportType())

    Public Shared Function Title(type As ReportType) As String
        Select Case type
            Case ReportType.InventoryList : Return "Inventory List Report"
            Case ReportType.LowStock : Return "Low-Stock Report"
            Case ReportType.OutOfStock : Return "Out-of-Stock Report"
            Case ReportType.Expiring : Return "Expiring Items Report"
            Case ReportType.Category : Return "Category Report"
            Case ReportType.PetType : Return "Pet Type Report"
            Case ReportType.Movement : Return "Inventory Movement Report"
            Case Else : Return "Inventory Summary Report"
        End Select
    End Function

    ''' <summary>
    ''' False for reports that describe current stock only. The system keeps no stock snapshots,
    ''' so those reports cannot be filtered by date without misrepresenting the data.
    ''' </summary>
    Public Shared Function UsesDateRange(type As ReportType) As Boolean
        Select Case type
            Case ReportType.Summary, ReportType.InventoryList, ReportType.Expiring, ReportType.Movement : Return True
            Case Else : Return False
        End Select
    End Function

    ''' <summary>Which stored date the From / To range is compared with.</summary>
    Public Shared Function DateBasis(type As ReportType) As String
        Select Case type
            Case ReportType.Summary
                Return "Stock figures are the current inventory. The date range applies to the period activity: items added (date added) and stock movements (transaction date)."
            Case ReportType.InventoryList
                Return "The date range is compared with the date each item was added."
            Case ReportType.Expiring
                Return "The date range is compared with each item's expiration date."
            Case ReportType.Movement
                Return "The date range is compared with each stock transaction's date (date received for Stock In)."
            Case Else
                Return "Shows current stock. The date range does not apply because the system does not keep past stock levels."
        End Select
    End Function
End Class

Public Class ReportCriteria
    Public Property Type As ReportType
    Public Property FromDate As Date?
    Public Property ToDate As Date?
    ''' <summary>Nothing = all categories.</summary>
    Public Property CategoryId As Integer?
    ''' <summary>Nothing = all pet types. See PetTypes.Matches.</summary>
    Public Property PetType As String
End Class

Public Class ReportColumn
    Public Property Header As String
    Public Property Kind As ReportValueKind
    ''' <summary>Relative width (star units).</summary>
    Public Property Width As Double

    Public Sub New(header As String, kind As ReportValueKind, Optional width As Double = 1)
        Me.Header = header
        Me.Kind = kind
        Me.Width = width
    End Sub

    Public ReadOnly Property IsNumeric As Boolean
        Get
            Return Kind <> ReportValueKind.Text AndAlso Kind <> ReportValueKind.Date
        End Get
    End Property
End Class

''' <summary>One table of a report. Rows hold raw values (Decimal, Integer, Date, String or Nothing).</summary>
Public Class ReportTable
    Public Property Title As String
    Public Property Columns As New List(Of ReportColumn)
    Public Property Rows As New List(Of Object())
    ''' <summary>Optional totals row, same shape as Rows.</summary>
    Public Property Totals As Object()

    Public Function FormatRow(row As Object()) As String()
        Return Columns.Select(Function(c, i) ReportFormat.Display(If(i < row.Length, row(i), Nothing), c.Kind)).ToArray()
    End Function

    ''' <summary>The totals row formatted for display; cells without a total stay blank (no "—").</summary>
    Public Function FormatTotals() As String()
        Return Columns.Select(Function(c, i) If(i < Totals.Length AndAlso Totals(i) IsNot Nothing, ReportFormat.Display(Totals(i), c.Kind), "")).ToArray()
    End Function
End Class

''' <summary>A headline figure shown above the tables, grouped by section.</summary>
Public Class ReportStat
    Public Property Section As String
    Public Property Label As String
    Public Property Value As Object
    Public Property Kind As ReportValueKind

    Public Sub New(section As String, label As String, value As Object, kind As ReportValueKind)
        Me.Section = section
        Me.Label = label
        Me.Value = value
        Me.Kind = kind
    End Sub

    Public ReadOnly Property ValueText As String
        Get
            Return ReportFormat.Display(Value, Kind)
        End Get
    End Property
End Class

''' <summary>A generated report. The screen, printout, PDF and Excel file are all produced from this one object.</summary>
Public Class ReportResult
    Public Property Criteria As ReportCriteria
    Public Property ShopName As String = AppSettings.DefaultAppName
    Public Property Title As String = ""
    Public Property PeriodText As String = ""
    Public Property FilterText As String = ""
    Public Property DateBasisNote As String = ""
    Public Property GeneratedAt As Date
    Public Property Stats As New List(Of ReportStat)
    Public Property Tables As New List(Of ReportTable)
    Public Property Notes As New List(Of String)
    Public Property IsEmpty As Boolean
    Public Property EmptyMessage As String = "No records match the selected report type, date range and filters."

    Public ReadOnly Property GeneratedText As String
        Get
            Return Fmt.DateTimeText(GeneratedAt)
        End Get
    End Property

    ''' <summary>Widest table, used to choose landscape pages for print and PDF.</summary>
    Public ReadOnly Property MaxColumns As Integer
        Get
            Return If(Tables.Count = 0, 0, Tables.Max(Function(t) t.Columns.Count))
        End Get
    End Property

    ''' <summary>Safe default file name, e.g. "PawStock Inventory Movement Report 2026-10-01 to 2026-10-31".</summary>
    Public Function SuggestedFileName() As String
        Dim name = $"{ShopName} {Title}"
        If Criteria IsNot Nothing AndAlso ReportTypes.UsesDateRange(Criteria.Type) AndAlso Criteria.FromDate.HasValue AndAlso Criteria.ToDate.HasValue Then
            name &= $" {Criteria.FromDate.Value:yyyy-MM-dd} to {Criteria.ToDate.Value:yyyy-MM-dd}"
        Else
            name &= $" {GeneratedAt:yyyy-MM-dd}"
        End If
        For Each c In IO.Path.GetInvalidFileNameChars()
            name = name.Replace(c, "-"c)
        Next
        Return name
    End Function
End Class

''' <summary>Display formatting for report values, built on Fmt so reports match the rest of the app.</summary>
Public NotInheritable Class ReportFormat
    Private Sub New()
    End Sub

    Public Shared Function Display(value As Object, kind As ReportValueKind) As String
        If value Is Nothing Then Return If(kind = ReportValueKind.Date, "—", "")
        ' Labels such as "Total" can sit in any column of a totals row.
        If TypeOf value Is String Then Return DirectCast(value, String)
        Select Case kind
            Case ReportValueKind.Count : Return Convert.ToInt32(value).ToString("#,##0", CultureInfo.InvariantCulture)
            Case ReportValueKind.Quantity : Return Fmt.Qty(Convert.ToDecimal(value))
            Case ReportValueKind.SignedQuantity : Return Fmt.SignedQty(Convert.ToDecimal(value))
            Case ReportValueKind.Money : Return Fmt.Money(Convert.ToDecimal(value))
            Case ReportValueKind.Date : Return Fmt.DateText(Convert.ToDateTime(value))
            Case Else : Return Convert.ToString(value, CultureInfo.InvariantCulture)
        End Select
    End Function

    ''' <summary>"Oct 27, 2026" for a single day, otherwise "Oct 1, 2026 – Oct 31, 2026".</summary>
    Public Shared Function Period(fromDate As Date, toDate As Date) As String
        If fromDate.Date = toDate.Date Then Return Fmt.DateText(fromDate)
        Return $"{Fmt.DateText(fromDate)} – {Fmt.DateText(toDate)}"
    End Function
End Class
