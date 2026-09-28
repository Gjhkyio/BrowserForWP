' BrowserForWP — draws a laid-out page into XAML.
'
' Output only: no script runs, nothing here reads back from what it drew. That is
' the whole difference between this engine and the WebView, and it is why this
' renderer is allowed to be simple.
'
' Every element it creates is positioned absolutely inside one Canvas, so a mistake
' here shows up as a box in the wrong place rather than as a reflow of everything
' after it.
'
' Draws: block backgrounds, one border outline per block, and every text run. Does
' not draw: images, shadows, radii, gradients, differing per-edge border styles (the
' widest visible edge wins), text selection, hover states.

Imports System.Globalization
Imports BrowserForWP.Core.Engine.Native
Imports Windows.UI.Text
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Media
Imports Windows.UI.Xaml.Shapes

Namespace Rendering

    ''' <summary>A laid-out page as a scrollable XAML element.</summary>
    Public NotInheritable Class XamlBoxRenderer

        Private Const FallbackTextColor As String = "#000000"
        Private Const TransparentColor As String = "transparent"
        Private Const NoBorderStyle As String = "none"

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Draw the laid-out tree. Returns Nothing for Nothing, so a caller can assign
        ''' the result to a host without a null check of its own.
        ''' </summary>
        Public Shared Function Render(root As LayoutBox) As ScrollViewer
            If root Is Nothing Then Return Nothing

            Dim canvas As New Canvas()
            canvas.Width = Math.Max(1, root.WidthPx)
            canvas.Height = Math.Max(1, root.HeightPx)
            AppendBox(canvas, root)

            Dim host As New ScrollViewer()
            host.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            host.VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            host.Content = canvas
            Return host
        End Function

        Private Shared Sub AppendBox(canvas As Canvas, box As LayoutBox)
            If canvas Is Nothing OrElse box Is Nothing Then Return

            AppendBoxDecoration(canvas, box)
            If box.Kind = LayoutBoxKind.TextRun Then
                AppendTextRun(canvas, box)
                Return
            End If
            For Each childBox In box.Children
                AppendBox(canvas, childBox)
            Next
        End Sub

        ''' <summary>Background fill, then a border outline when the page asked for one.</summary>
        Private Shared Sub AppendBoxDecoration(canvas As Canvas, box As LayoutBox)
            If box.Style Is Nothing Then Return
            If box.Kind <> LayoutBoxKind.Block Then Return
            If box.WidthPx <= 0 OrElse box.HeightPx <= 0 Then Return

            Dim backgroundCss As String = box.Style.BackgroundColor
            If Not String.IsNullOrEmpty(backgroundCss) AndAlso backgroundCss <> TransparentColor Then
                Dim fillShape As New Rectangle()
                fillShape.Width = box.WidthPx
                fillShape.Height = box.HeightPx
                fillShape.Fill = New SolidColorBrush(ParseCssColor(backgroundCss, TransparentBrushColor()))
                Canvas.SetLeft(fillShape, box.XPx)
                Canvas.SetTop(fillShape, box.YPx)
                canvas.Children.Add(fillShape)
            End If

            ' One outline for the box, not four edges: the subset this engine claims
            ' does not include differing per-edge styles, and when they agree four
            ' rectangles look exactly like one outline.
            Dim nodeStyle As ComputedStyle = box.Style
            Dim horizontalEdge As Double = Math.Max(nodeStyle.BorderLeftWidthPx, nodeStyle.BorderRightWidthPx)
            Dim verticalEdge As Double = Math.Max(nodeStyle.BorderTopWidthPx, nodeStyle.BorderBottomWidthPx)
            Dim edgeWidth As Double = Math.Max(horizontalEdge, verticalEdge)
            If edgeWidth <= 0 Then Return

            Dim topVisible As Boolean = nodeStyle.BorderTopStyle <> NoBorderStyle
            Dim leftVisible As Boolean = nodeStyle.BorderLeftStyle <> NoBorderStyle
            Dim rightVisible As Boolean = nodeStyle.BorderRightStyle <> NoBorderStyle
            Dim bottomVisible As Boolean = nodeStyle.BorderBottomStyle <> NoBorderStyle
            If Not (topVisible OrElse leftVisible OrElse rightVisible OrElse bottomVisible) Then Return

            Dim outlineShape As New Rectangle()
            outlineShape.Width = box.WidthPx
            outlineShape.Height = box.HeightPx
            outlineShape.StrokeThickness = edgeWidth
            outlineShape.Stroke = New SolidColorBrush(ResolveBorderColor(nodeStyle))
            Canvas.SetLeft(outlineShape, box.XPx)
            Canvas.SetTop(outlineShape, box.YPx)
            canvas.Children.Add(outlineShape)
        End Sub

        Private Shared Sub AppendTextRun(canvas As Canvas, box As LayoutBox)
            If String.IsNullOrEmpty(box.Text) Then Return

            Dim run As New TextBlock()
            run.Text = box.Text
            run.TextWrapping = TextWrapping.NoWrap
            If box.Style IsNot Nothing Then
                run.FontSize = box.Style.FontSizePx
                If box.Style.FontWeight >= 600 Then
                    run.FontWeight = FontWeights.Bold
                Else
                    run.FontWeight = FontWeights.Normal
                End If
                If box.Style.FontStyle = "italic" Then
                    run.FontStyle = Windows.UI.Text.FontStyle.Italic
                Else
                    run.FontStyle = Windows.UI.Text.FontStyle.Normal
                End If
                run.Foreground = New SolidColorBrush(ParseCssColor(box.Style.Color, ParseCssColor(FallbackTextColor, TransparentBrushColor())))
            End If
            Canvas.SetLeft(run, box.XPx)
            Canvas.SetTop(run, box.YPx)
            canvas.Children.Add(run)
        End Sub

        ''' <summary>
        ''' A border colour of "currentcolor" means the text colour, which is what the
        ''' cascade already resolved into Color.
        ''' </summary>
        Private Shared Function ResolveBorderColor(nodeStyle As ComputedStyle) As Windows.UI.Color
            If nodeStyle Is Nothing Then Return TransparentBrushColor()
            If nodeStyle.BorderTopColor = "currentcolor" Then
                Return ParseCssColor(nodeStyle.Color, ParseCssColor(FallbackTextColor, TransparentBrushColor()))
            End If
            Return ParseCssColor(nodeStyle.BorderTopColor, ParseCssColor(FallbackTextColor, TransparentBrushColor()))
        End Function

        Private Shared Function TransparentBrushColor() As Windows.UI.Color
            Return Windows.UI.Color.FromArgb(0, 0, 0, 0)
        End Function

        ''' <summary>
        ''' #rrggbb and #rgb only, plus "transparent". An unknown value returns the
        ''' fallback rather than throwing: a page with one odd colour must not cost the
        ''' whole render.
        ''' </summary>
        Private Shared Function ParseCssColor(value As String, fallback As Windows.UI.Color) As Windows.UI.Color
            If String.IsNullOrEmpty(value) Then Return fallback
            Dim trimmed As String = value.Trim()
            If trimmed = TransparentColor Then Return TransparentBrushColor()
            If Not trimmed.StartsWith("#") Then Return fallback

            Dim hex As String = trimmed.Substring(1)
            If hex.Length = 3 Then
                hex = hex.Substring(0, 1) & hex.Substring(0, 1) &
                      hex.Substring(1, 1) & hex.Substring(1, 1) &
                      hex.Substring(2, 1) & hex.Substring(2, 1)
            End If
            If hex.Length <> 6 Then Return fallback

            Dim redValue As Integer = 0
            Dim greenValue As Integer = 0
            Dim blueValue As Integer = 0
            If Not Integer.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, redValue) Then Return fallback
            If Not Integer.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, greenValue) Then Return fallback
            If Not Integer.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, blueValue) Then Return fallback

            Return Windows.UI.Color.FromArgb(255, CByte(redValue), CByte(greenValue), CByte(blueValue))
        End Function

    End Class

End Namespace
