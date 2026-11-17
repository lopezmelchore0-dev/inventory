Imports System.Windows.Media.Animation
Imports System.Windows.Threading

''' <summary>
''' Help &amp; Support: opens on a preview (intro + topic cards) and switches to the full guide on
''' "View Full Guide" or a topic card. Both views are built from <see cref="HelpContent.Topics"/>.
''' </summary>
Class HelpView

    Public Shared ReadOnly TopicColumnsProperty As DependencyProperty =
        DependencyProperty.Register(NameOf(TopicColumns), GetType(Integer), GetType(HelpView), New PropertyMetadata(4))

    ''' <summary>Number of topic card columns on the preview; follows the page width.</summary>
    Public Property TopicColumns As Integer
        Get
            Return CInt(GetValue(TopicColumnsProperty))
        End Get
        Set(value As Integer)
            SetValue(TopicColumnsProperty, value)
        End Set
    End Property

    Public Sub New()
        InitializeComponent()
        IntroText.Text = HelpContent.Intro
        TopicList.ItemsSource = HelpContent.Topics
        TocList.ItemsSource = HelpContent.Topics
        GuideSections.ItemsSource = HelpContent.Topics
    End Sub

    Public ReadOnly Property IsGuideOpen As Boolean
        Get
            Return GuidePanel.Visibility = Visibility.Visible
        End Get
    End Property

    ' ===================== Preview / guide switching =====================

    ''' <summary>Shows the preview (intro and topic cards), scrolled to the top.</summary>
    Public Sub ShowPreview()
        Dim wasGuide = IsGuideOpen
        GuidePanel.Visibility = Visibility.Collapsed
        PreviewPanel.Visibility = Visibility.Visible
        PreviewPanel.ScrollToTop()
        If wasGuide Then FadeIn(PreviewPanel)
        FocusAfterLayout(PreviewPanel)
    End Sub

    ''' <summary>Shows the full guide, scrolled to <paramref name="topic"/> or to the top.</summary>
    Public Sub ShowGuide(Optional topic As HelpTopic = Nothing)
        Dim wasGuide = IsGuideOpen
        PreviewPanel.Visibility = Visibility.Collapsed
        GuidePanel.Visibility = Visibility.Visible
        If Not wasGuide Then FadeIn(GuidePanel)
        If topic Is Nothing Then
            GuideScroll.ScrollToTop()
        Else
            ' Sections are measured only once the guide is visible, so scroll after layout.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, New Action(Sub() ScrollToTopic(topic)))
        End If
        FocusAfterLayout(GuideScroll)
    End Sub

    Private Sub ScrollToTopic(topic As HelpTopic)
        GuideScroll.UpdateLayout()
        Dim section = TryCast(GuideSections.ItemContainerGenerator.ContainerFromItem(topic), FrameworkElement)
        If section Is Nothing OrElse Not section.IsDescendantOf(GuideContent) Then
            GuideScroll.ScrollToTop()
            Return
        End If
        Dim offset = section.TransformToAncestor(GuideContent).Transform(New Point(0, 0)).Y
        GuideScroll.ScrollToVerticalOffset(offset)
    End Sub

    Private Shared Sub FadeIn(panel As UIElement)
        panel.BeginAnimation(OpacityProperty, New DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)))
    End Sub

    ''' <summary>Gives keyboard focus to the visible panel so Esc and the scroll keys work right away.</summary>
    Private Sub FocusAfterLayout(target As UIElement)
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, New Action(Sub() If target.IsVisible Then target.Focus()))
    End Sub

    ' ===================== Events =====================

    Private Sub ViewGuide_Click(sender As Object, e As RoutedEventArgs)
        ShowGuide()
    End Sub

    Private Sub Topic_Click(sender As Object, e As RoutedEventArgs)
        ShowGuide(TryCast(DirectCast(sender, FrameworkElement).Tag, HelpTopic))
    End Sub

    Private Sub BackToOverview_Click(sender As Object, e As RoutedEventArgs)
        ShowPreview()
    End Sub

    Private Sub BackToTop_Click(sender As Object, e As RoutedEventArgs)
        GuideScroll.ScrollToTop()
    End Sub

    Private Sub HelpView_PreviewKeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Escape AndAlso IsGuideOpen Then
            ShowPreview()
            e.Handled = True
        End If
    End Sub

    ' ===================== Responsive layout =====================

    Private Sub LayoutRoot_SizeChanged(sender As Object, e As SizeChangedEventArgs)
        Dim width = LayoutRoot.ActualWidth
        TopicColumns = If(width >= 1000, 4, If(width >= 640, 2, 1))
        BannerArt.Visibility = If(width >= 720, Visibility.Visible, Visibility.Collapsed)

        Dim showToc = width >= 960
        TocPanel.Visibility = If(showToc, Visibility.Visible, Visibility.Collapsed)
        TocColumn.Width = New GridLength(If(showToc, 240, 0))
        TocGap.Width = New GridLength(If(showToc, 16, 0))
    End Sub
End Class
