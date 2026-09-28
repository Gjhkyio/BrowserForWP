' BrowserForWP — text measurement by arithmetic.
'
' Not the renderer's measurer: the app uses XamlTextMeasurer. This one exists so
' that layout is testable off-device, and so that any caller which needs the same
' answer twice gets it. Its two constants are asserted by
' tools/proto/textmeasure.mjs, which hardcodes the same values.

Namespace Engine.Native

    ''' <summary>Measures text as 0.5 em per character. Deterministic.</summary>
    Public NotInheritable Class FixedAdvanceTextMeasurer
        Implements ITextMeasurer

        ''' <summary>Ems per character. tools/proto/boxlayout.mjs assumes the same.</summary>
        Public Const AdvanceFactor As Double = 0.5

        ''' <summary>CSS's "normal" line-height, as a multiple of the font size.</summary>
        Public Const NormalLineHeightFactor As Double = 1.2

        Public Function MeasureWidth(text As String, style As ComputedStyle) As Double Implements ITextMeasurer.MeasureWidth
            If String.IsNullOrEmpty(text) OrElse style Is Nothing Then Return 0
            Return text.Length * style.FontSizePx * AdvanceFactor
        End Function

        Public Function LineHeight(style As ComputedStyle) As Double Implements ITextMeasurer.LineHeight
            If style Is Nothing Then Return 0
            If style.LineHeightPx > 0 Then Return style.LineHeightPx
            Return style.FontSizePx * NormalLineHeightFactor
        End Function

    End Class

End Namespace
