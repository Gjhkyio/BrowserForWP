' BrowserForWP — which engine renders the next page.
'
' Two questions, one answer: what the user asked for, and what a measurement of
' the page actually says. The rule lives here, in Core, because it is pure — no
' XAML, no prose the user reads, no I/O — so tools/proto/engine-choice.mjs can
' execute it off-device and tests/BrowserForWP.Core.Tests can compile it.
'
' The row of the table that matters most is the one about an ABSENT measurement.
' ProbeReport.CouldRun = False means nothing was measured, and this project has
' already shipped one lie of that exact shape: a probe that never ran was
' reported in the UI as "no missing web features detected". An unmeasured page
' therefore stays on the engine that exists, and never switches to the one that
' does not.

Namespace Engine

    ''' <summary>
    ''' The engine-selection and automatic-fallback rule, as data. Uninstantiable:
    ''' it is a rule, not a thing.
    ''' </summary>
    Public NotInheritable Class EngineChoice

        Public Const Trident As String = "trident"
        Public Const Native As String = "native"
        Public Const Auto As String = "auto"

        ''' <summary>
        ''' The CompatibilityProbe count at which an automatic choice gives up on
        ''' Trident. Eight is deliberately the same number the shell's reader
        ''' fallback already uses, so the two thresholds cannot come to disagree
        ''' about what "too broken to read" means.
        ''' </summary>
        Public Const AutomaticFallbackThreshold As Integer = 8

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Anything unrecognised is Auto: never an error, and never Native by
        ''' accident. A corrupt setting must not change which engine renders.
        ''' </summary>
        Public Shared Function Normalize(setting As String) As String
            If setting = Trident Then Return Trident
            If setting = Native Then Return Native
            Return Auto
        End Function

        ''' <summary>
        ''' The engine to use. An explicit setting always wins over the probe: a
        ''' user who chose the native engine gets it even where Trident would have
        ''' coped, and a user who chose Trident keeps it even where it will
        ''' struggle. Only Auto consults the measurement.
        ''' </summary>
        Public Shared Function Decide(setting As String, probeMeasured As Boolean, missingFeatureCount As Integer) As String
            Dim wanted As String = Normalize(setting)
            If wanted = Native Then Return Native
            If wanted = Trident Then Return Trident
            If Not probeMeasured Then Return Trident
            If missingFeatureCount >= AutomaticFallbackThreshold Then Return Native
            Return Trident
        End Function

        ''' <summary>
        ''' Why Decide returned what it did, as a resource key. Never a sentence:
        ''' Core has no business holding user-facing prose, and the device has to
        ''' say this in two languages. The view layer resolves it through Localizer.
        ''' </summary>
        Public Shared Function Explain(setting As String, probeMeasured As Boolean, missingFeatureCount As Integer) As String
            Dim wanted As String = Normalize(setting)
            If wanted = Native Then Return "EngineReasonSettingNative"
            If wanted = Trident Then Return "EngineReasonSetting"
            If Not probeMeasured Then Return "EngineReasonAutoNoMeasurement"
            If missingFeatureCount >= AutomaticFallbackThreshold Then Return "EngineReasonAutoTooManyMissingFeatures"
            Return "EngineReasonAutoFits"
        End Function

    End Class

End Namespace
