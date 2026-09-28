' BrowserForWP — the text-measurement seam.
'
' Layout needs to know how wide a string is before it can break a line, and only
' the view layer can answer that: measuring text is a XAML operation. So Core asks
' the question and the app answers it, exactly as IBrowserEngine does for the
' engine itself.
'
' Two implementations exist on purpose:
'   * FixedAdvanceTextMeasurer -- arithmetic, so the numbers layout produces are
'     reproducible in tools/proto/boxlayout.mjs off-device.
'   * BrowserForWP.Rendering.XamlTextMeasurer -- the real one, a TextBlock probe.

Namespace Engine.Native

    ''' <summary>Measures text. The one thing layout cannot compute by itself.</summary>
    Public Interface ITextMeasurer

        ''' <summary>
        ''' Width of text in device-independent pixels when it is not broken: one
        ''' line, no wrapping.
        ''' </summary>
        Function MeasureWidth(text As String, style As ComputedStyle) As Double

        ''' <summary>
        ''' Height of one line box for this style: the resolved line-height when the
        ''' page set one, and 1.2 x font-size when it did not (CSS's "normal").
        ''' </summary>
        Function LineHeight(style As ComputedStyle) As Double

    End Interface

End Namespace
