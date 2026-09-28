' BrowserForWP — Windows Phone 8.1's only available engine.
'
' This class is the honest answer to "why not Chromium or Firefox": the platform
' provides one rendering engine and no mechanism to substitute it. What it CAN
' do is report its limits precisely, so the user gets an explanation rather than
' a mystery.

Imports System.Threading.Tasks
Imports Windows.UI.Xaml.Controls

Namespace Engine

    ''' <summary>
    ''' Trident (IE11) hosted in the system WebView.
    ''' </summary>
    Public NotInheritable Class TridentEngine
        Implements IBrowserEngine

        Private ReadOnly _view As WebView

        Public Sub New()
            _view = New WebView()
        End Sub

        ''' <summary>The WebView itself, for the view layer to host.</summary>
        Public ReadOnly Property View As WebView
            Get
                Return _view
            End Get
        End Property

        ''' <summary>
        ''' Reported as measured on Windows Phone 8.1, not as desired:
        '''   SupportsTls13          - Schannel on WP8.1 tops out at TLS 1.2, and the
        '''                            WebView goes through Schannel. The app's own
        '''                            transport (BrowserForWP.Net) does speak TLS 1.3,
        '''                            but this engine's traffic never uses it.
        '''   SupportsModernJavaScript - IE11 has no ES6+, no modules, no async/await.
        '''   SupportsFetch          - IE11 has no fetch.
        ''' </summary>
        Public ReadOnly Property Capabilities As EngineCapabilities Implements IBrowserEngine.Capabilities
            Get
                Return New EngineCapabilities With {
                    .Name = "System WebView",
                    .RenderingEngine = "Trident (IE11)",
                    .SupportsTls13 = False,
                    .SupportsModernJavaScript = False,
                    .SupportsWebSocket = True,
                    .SupportsFetch = False
                }
            End Get
        End Property

        Public ReadOnly Property Source As Object Implements IBrowserEngine.Source
            Get
                Return _view
            End Get
        End Property

        Public Sub Navigate(url As String) Implements IBrowserEngine.Navigate
            If String.IsNullOrEmpty(url) Then Return
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(url, UriKind.Absolute, uri) Then Return
            _view.Navigate(uri)
        End Sub

        Public Sub GoBack() Implements IBrowserEngine.GoBack
            If _view.CanGoBack Then _view.GoBack()
        End Sub

        Public Sub GoForward() Implements IBrowserEngine.GoForward
            If _view.CanGoForward Then _view.GoForward()
        End Sub

        Public Sub Reload() Implements IBrowserEngine.Reload
            _view.Refresh()
        End Sub

        Public Sub [Stop]() Implements IBrowserEngine.Stop
            ' WP8.1's WebView exposes no cancellation primitive. Stopping is
            ' expressed by no longer acting on the in-flight navigation, which the
            ' view layer handles by checking the navigation token before applying
            ' progress updates. Recorded here so the behaviour is not mistaken for
            ' an oversight.
        End Sub

        Public Function InvokeScriptAsync(script As String) As Task(Of String) Implements IBrowserEngine.InvokeScriptAsync
            Return _view.InvokeScriptAsync("eval", New String() {script}).AsTask()
        End Function
    End Class

End Namespace
