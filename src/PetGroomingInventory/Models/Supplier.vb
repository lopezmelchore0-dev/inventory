Public Class Supplier
    Public Property Id As Integer
    Public Property Name As String = ""
    Public Property ContactPerson As String = ""
    Public Property Phone As String = ""
    Public Property Address As String = ""
    Public Property Email As String
    Public Property IsSample As Boolean
    Public Property ItemCount As Integer

    Public ReadOnly Property EmailText As String
        Get
            Return If(String.IsNullOrWhiteSpace(Email), "—", Email)
        End Get
    End Property

    ''' <summary>Label used in supplier pickers, so sample records are recognisable.</summary>
    Public ReadOnly Property DisplayName As String
        Get
            Return If(IsSample, Name & "  (Sample)", Name)
        End Get
    End Property

    Public ReadOnly Property ItemCountText As String
        Get
            Return Fmt.Plural(ItemCount, "item")
        End Get
    End Property
End Class
