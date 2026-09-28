' BrowserForWP — this repository's own engine, behind the same seam as Trident.
'
' It fetches a page over the app's own TLS 1.3 transport, builds the box tree,
' lays it out and paints it into XAML. What it is NOT is a browser: no
' JavaScript, no float or position, no collapsed margins, no images. The honest
' summary of the subset is in docs/ARCHITECTURE.md, and the deferred list is in
' MAINTAINING.md.
'
' Two decisions in this class are deliberate and worth stating where they are
' made, because both are claims:
'
'  * Capabilities.SupportsTls13 = True is claimed here and nowhere else in the
'    product. It is true because the fetch goes through BrowserForWP.Net's own
'    TLS 1.3 client instead of Schannel, which is exactly what the system
'    WebView cannot do. This is the only engine that may honestly say it.
'  * SupportsScripting = False, which makes NeedsPolyfillLayer False as well.
'    There is no document object here to inject a compatibility layer into, and
'    an engine that claimed otherwise would have the shell running a script in a
'    place that has no script host.
'
' It carries no user-facing prose: it reports a state and the shell localizes it.

Imports System.Threading.Tasks
Imports BrowserForWP.Core.Engine
Imports BrowserForWP.Core.Engine.Native
Imports BrowserForWP.Core.Storage
Imports Windows.UI.Xaml.Controls

Namespace Engine

    ''' <summary>
    ''' The outcome of one render, for the shell to translate. ErrorKind is a token
    ''' from a closed set — never a sentence, and never empty when IsSuccess is
    ''' False.
    ''' </summary>
    Public NotInheritable Class NativeNavigationResult

        Public Property Url As String
        Public Property IsSuccess As Boolean

        ''' <summary>One of "", "Fetch", "NotHtml", "Layout".</summary>
        Public Property ErrorKind As String = String.Empty

        ''' <summary>Detail for the token; engine-level text, shown beside localized copy.</summary>
        Public Property ErrorMessage As String = String.Empty

        Public Property WidthPx As Double

        Public Property HeightPx As Double

    End Class

    ''' <summary>
    ''' The renderer this repository wrote, answering the seam the platform's
    ''' WebView answers.
    ''' </summary>
    Public NotInheritable Class NativeEngine
        Implements IBrowserEngine

        Private ReadOnly _host As New Border()
        Private ReadOnly _fetcher As IDocumentFetcher
        Private ReadOnly _settings As AppSettings

        Private _currentUrl As String = String.Empty
        Private _lastError As String = String.Empty
        Private _lastSize As String = String.Empty

        ''' <summary>
        ''' The settings object is held rather than flattened to a string, so a DoH
        ''' server typed into Settings applies to the next fetch instead of to the
        ''' next app launch.
        ''' </summary>
        Public Sub New(fetcher As IDocumentFetcher, settings As AppSettings)
            _fetcher = fetcher
            _settings = settings
        End Sub

        ''' <summary>Raised when a render ends, successfully or not.</summary>
        Public Event Navigated As EventHandler(Of NativeNavigationResult)

        ''' <summary>
        ''' Measured claims about this engine, not aspirations. Nothing here is
        ''' guessed: each flag names a mechanism this class actually has or lacks.
        ''' </summary>
        Public ReadOnly Property Capabilities As EngineCapabilities Implements IBrowserEngine.Capabilities
            Get
                Return New EngineCapabilities With {
                    .Name = "BrowserForWP native",
                    .RenderingEngine = "BlockLayout + XamlBoxRenderer",
                    .SupportsTls13 = True,
                    .SupportsScripting = False,
                    .SupportsModernJavaScript = False,
                    .SupportsWebSocket = False,
                    .SupportsFetch = False
                }
            End Get
        End Property

        ''' <summary>
        ''' One Border, created here and returned on every call. The shell takes this
        ''' object once and keeps it in ContentHost, so the host must not be replaced
        ''' between renders; each render replaces its Child instead.
        ''' </summary>
        Public ReadOnly Property Source As Object Implements IBrowserEngine.Source
            Get
                Return _host
            End Get
        End Property

        Public ReadOnly Property CurrentUrl As String
            Get
                Return _currentUrl
            End Get
        End Property

        Public ReadOnly Property LastError As String
            Get
                Return _lastError
            End Get
        End Property

        ''' <summary>"width x height  boxes" for the diagnostics line; empty until a render succeeds.</summary>
        Public ReadOnly Property LastSize As String
            Get
                Return _lastSize
            End Get
        End Property

        Public Sub Navigate(url As String) Implements IBrowserEngine.Navigate
            If String.IsNullOrEmpty(url) Then Return
            RenderInBackground(url)
        End Sub

        ''' <summary>
        ''' Re-render the current page. There is no back stack in this class: history
        ''' belongs to BrowserSession in the shell, which is the only thing that knows
        ''' what a "previous page" is across tabs.
        ''' </summary>
        Public Sub GoBack() Implements IBrowserEngine.GoBack
            RenderInBackground(_currentUrl)
        End Sub

        Public Sub GoForward() Implements IBrowserEngine.GoForward
            RenderInBackground(_currentUrl)
        End Sub

        Public Sub Reload() Implements IBrowserEngine.Reload
            RenderInBackground(_currentUrl)
        End Sub

        ''' <summary>
        ''' Nothing to stop: the fetch is not cancellable through IDocumentFetcher,
        ''' and layout of one document is fast enough to be invisible on this
        ''' hardware. Recorded here rather than left to look like an oversight.
        ''' </summary>
        Public Sub [Stop]() Implements IBrowserEngine.Stop
        End Sub

        ''' <summary>
        ''' Always an empty string. The interface needs an implementation and the
        ''' shell never calls it on an engine with no script host.
        ''' </summary>
        Public Function InvokeScriptAsync(script As String) As Task(Of String) Implements IBrowserEngine.InvokeScriptAsync
            Return Task.FromResult(String.Empty)
        End Function

        ''' <summary>
        ''' Fetch, build, lay out and paint. Returns False when nothing was drawn.
        ''' Never throws: a page that will not render is reported as a result, because
        ''' it must not be able to take the shell down with it.
        ''' </summary>
        Public Async Function RenderAsync(url As String) As Task(Of Boolean)
            _currentUrl = url
            _lastError = String.Empty
            _lastSize = String.Empty

            Try
                Dim response As DocumentResponse = Await _fetcher.FetchAsync(url, _settings.DohUrl)
                If Not String.IsNullOrEmpty(response.ErrorMessage) Then
                    Return Finish(url, False, "Fetch", response.ErrorMessage, 0, 0)
                End If
                If Not response.IsHtml Then
                    Return Finish(url, False, "NotHtml", String.Empty, 0, 0)
                End If

                ' The viewport is the host's real width. Before the first layout pass
                ' that reading is 0, and the fallback keeps a render useful rather
                ' than one pixel wide.
                Dim viewportPx As Double = _host.ActualWidth
                If viewportPx < 1 Then viewportPx = 360

                Dim boxTree As BoxNode = BoxTreeBuilder.BuildPage(response.Text, BoxTreeBuilder.PageCss(response.Text))
                Dim measurer As New BrowserForWP.Rendering.XamlTextMeasurer()
                Dim laidOut As LayoutBox = BlockLayout.Layout(boxTree, viewportPx, measurer)
                _host.Child = BrowserForWP.Rendering.XamlBoxRenderer.Render(laidOut)
                _lastSize = CInt(laidOut.WidthPx).ToString() & " x " & CInt(laidOut.HeightPx).ToString() &
                            "  " & laidOut.DescendantCount().ToString()
                Return Finish(url, True, String.Empty, String.Empty, laidOut.WidthPx, laidOut.HeightPx)
            Catch ex As Exception
                Return Finish(url, False, "Layout", ex.Message, 0, 0)
            End Try
        End Function

        Private Function Finish(url As String, succeeded As Boolean, errorKind As String, message As String, widthPx As Double, heightPx As Double) As Boolean
            _lastError = message
            Dim outcome As New NativeNavigationResult With {
                .Url = url,
                .IsSuccess = succeeded,
                .ErrorKind = errorKind,
                .ErrorMessage = message,
                .WidthPx = widthPx,
                .HeightPx = heightPx
            }
            RaiseEvent Navigated(Me, outcome)
            Return succeeded
        End Function

        Private Async Sub RenderInBackground(url As String)
            If String.IsNullOrEmpty(url) Then Return
            ' RenderAsync catches everything, so there is no exception path here.
            Await RenderAsync(url)
        End Sub

    End Class

End Namespace
