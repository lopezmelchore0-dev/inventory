''' <summary>Shows validation messages under form fields and highlights the inputs.</summary>
Public Class FormErrors
    Private ReadOnly _fields As New Dictionary(Of String, (Label As TextBlock, Input As Control))
    Private ReadOnly _general As TextBlock

    Public Sub New(generalLabel As TextBlock)
        _general = generalLabel
    End Sub

    Public Sub Register(key As String, label As TextBlock, input As Control)
        _fields(key) = (label, input)
    End Sub

    Public Sub Clear()
        For Each field In _fields.Values
            field.Label.Text = ""
            field.Label.Visibility = Visibility.Collapsed
            field.Input.ClearValue(Control.BorderBrushProperty)
        Next
        If _general IsNot Nothing Then
            _general.Text = ""
            _general.Visibility = Visibility.Collapsed
        End If
    End Sub

    Public Sub Show(errors As Dictionary(Of String, String))
        Clear()
        Dim firstInput As Control = Nothing
        Dim unmatched As New List(Of String)
        For Each kv In errors
            Dim field As (Label As TextBlock, Input As Control) = Nothing
            If _fields.TryGetValue(kv.Key, field) Then
                field.Label.Text = kv.Value
                field.Label.Visibility = Visibility.Visible
                field.Input.BorderBrush = StatusPalette.Find("ErrorTextBrush")
                If firstInput Is Nothing Then firstInput = field.Input
            Else
                unmatched.Add(kv.Value)
            End If
        Next
        If unmatched.Count > 0 Then ShowGeneral(String.Join(Environment.NewLine, unmatched))
        firstInput?.Focus()
    End Sub

    Public Sub ShowGeneral(message As String)
        If _general Is Nothing Then
            Dialogs.ShowError("Please check your entry", message)
            Return
        End If
        _general.Text = message
        _general.Visibility = Visibility.Visible
    End Sub
End Class
