''' <summary>A circular progress ring (0–1), like the "Todays Activity" ring in the reference design.</summary>
Public Class RingChart
    Inherits FrameworkElement

    Public Shared ReadOnly ValueProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(Value), GetType(Double), GetType(RingChart), New FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender))

    Public Shared ReadOnly RingBrushProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(RingBrush), GetType(Brush), GetType(RingChart), New FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender))

    Public Shared ReadOnly TrackBrushProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(TrackBrush), GetType(Brush), GetType(RingChart), New FrameworkPropertyMetadata(Brushes.Lavender, FrameworkPropertyMetadataOptions.AffectsRender))

    Public Shared ReadOnly ThicknessProperty As DependencyProperty = DependencyProperty.Register(
        NameOf(Thickness), GetType(Double), GetType(RingChart), New FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender))

    Public Property Value As Double
        Get
            Return CDbl(GetValue(ValueProperty))
        End Get
        Set(v As Double)
            SetValue(ValueProperty, v)
        End Set
    End Property

    Public Property RingBrush As Brush
        Get
            Return CType(GetValue(RingBrushProperty), Brush)
        End Get
        Set(v As Brush)
            SetValue(RingBrushProperty, v)
        End Set
    End Property

    Public Property TrackBrush As Brush
        Get
            Return CType(GetValue(TrackBrushProperty), Brush)
        End Get
        Set(v As Brush)
            SetValue(TrackBrushProperty, v)
        End Set
    End Property

    Public Property Thickness As Double
        Get
            Return CDbl(GetValue(ThicknessProperty))
        End Get
        Set(v As Double)
            SetValue(ThicknessProperty, v)
        End Set
    End Property

    Protected Overrides Sub OnRender(dc As DrawingContext)
        Dim size = Math.Min(ActualWidth, ActualHeight)
        If size <= 0 Then Return
        Dim stroke = Math.Min(Thickness, size / 2)
        Dim radius = (size - stroke) / 2
        Dim center As New Point(ActualWidth / 2, ActualHeight / 2)

        dc.DrawEllipse(Nothing, New Pen(TrackBrush, stroke), center, radius, radius)

        Dim progress = Math.Max(0.0, Math.Min(1.0, Value))
        If progress <= 0 Then Return
        Dim ringPen As New Pen(RingBrush, stroke) With {.StartLineCap = PenLineCap.Round, .EndLineCap = PenLineCap.Round}
        If progress >= 0.9999 Then
            dc.DrawEllipse(Nothing, ringPen, center, radius, radius)
            Return
        End If

        Dim sweep = progress * 360
        Dim geometry As New StreamGeometry()
        Using ctx = geometry.Open()
            ctx.BeginFigure(PointOnCircle(center, radius, 0), False, False)
            ctx.ArcTo(PointOnCircle(center, radius, sweep), New Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, True, False)
        End Using
        geometry.Freeze()
        dc.DrawGeometry(Nothing, ringPen, geometry)
    End Sub

    ''' <summary>0 degrees = 12 o'clock, clockwise.</summary>
    Private Shared Function PointOnCircle(center As Point, radius As Double, degrees As Double) As Point
        Dim radians = (degrees - 90) * Math.PI / 180
        Return New Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians))
    End Function
End Class
