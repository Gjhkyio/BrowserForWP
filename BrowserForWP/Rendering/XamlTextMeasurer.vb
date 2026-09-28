' BrowserForWP — the real text measurer: XAML's own.
'
' Core declares ITextMeasurer and cannot implement it, because measuring text is a
' XAML operation and Core does not reference XAML. This is the app's answer, and it
' uses the same TextBlock the renderer will use, so the measurement and the drawing
' cannot disagree about a font.
'
' Must be called on the UI thread. Laying out a page is a UI-thread operation
' anyway, so no caller has to arrange that.

Imports BrowserForWP.Core.Engine.Native
Imports Windows.UI.Text
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Media

Namespace Rendering

    ''' <summary>Measures by asking a TextBlock, which is what will draw the text.</summary>
    Public NotInheritable Class XamlTextMeasurer
        Implements ITextMeasurer

        Public Function MeasureWidth(text As String, style As ComputedStyle) As Double Implements ITextMeasurer.MeasureWidth
            If String.IsNullOrEmpty(text) OrElse style Is Nothing Then Return 0
            Dim probe As TextBlock = BuildProbe(text, style)
            probe.Measure(New Windows.Foundation.Size(Double.PositiveInfinity, Double.PositiveInfinity))
            Return probe.DesiredSize.Width
        End Function

        Public Function LineHeight(style As ComputedStyle) As Double Implements ITextMeasurer.LineHeight
            If style Is Nothing Then Return 0
            If style.LineHeightPx > 0 Then Return style.LineHeightPx
            ' "normal" means "whatever this font needs", so ask the font rather than
            ' assuming 1.2 -- the constant is only a fallback for the fixed measurer.
            Dim probe As TextBlock = BuildProbe("Mg", style)
            probe.Measure(New Windows.Foundation.Size(Double.PositiveInfinity, Double.PositiveInfinity))
            Return probe.DesiredSize.Height
        End Function

        Private Shared Function BuildProbe(text As String, style As ComputedStyle) As TextBlock
            Dim probe As New TextBlock()
            probe.Text = text
            probe.FontFamily = New FontFamily(CleanFontFamily(style.FontFamily))
            probe.FontSize = style.FontSizePx
            If style.FontWeight >= 600 Then
                probe.FontWeight = FontWeights.Bold
            Else
                probe.FontWeight = FontWeights.Normal
            End If
            ' NOT FontStyles.Italic: that helper exists in WPF, not in the WinRT
            ' profile Windows Phone 8.1 compiles against (BC30451). XAML markup
            ' resolves FontStyle="Italic" through the enum; code has to name it.
            If style.FontStyle = "italic" Then
                probe.FontStyle = Windows.UI.Text.FontStyle.Italic
            Else
                probe.FontStyle = Windows.UI.Text.FontStyle.Normal
            End If
            Return probe
        End Function

        ''' <summary>
        ''' CSS font stacks are quoted and comma separated ("'Segoe UI',Arial"); XAML
        ''' takes one family name. Unquoted, first entry, or a safe default.
        ''' </summary>
        Private Shared Function CleanFontFamily(cssFamily As String) As String
            If String.IsNullOrEmpty(cssFamily) Then Return "Segoe UI"
            Dim firstName As String = cssFamily.Split(","c)(0).Trim()
            firstName = firstName.Replace("'"c, " "c)
            firstName = firstName.Replace(ChrW(34), " "c)
            firstName = firstName.Trim()
            If firstName.Length = 0 Then Return "Segoe UI"
            Return firstName
        End Function

    End Class

End Namespace
