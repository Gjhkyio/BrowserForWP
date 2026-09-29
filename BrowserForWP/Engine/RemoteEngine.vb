' BrowserForWP — the remote engine, reduced to its seam.
'
' This file is a skeleton on purpose: the transport arrives in a later task, and
' until it does the honest thing for this engine to do is report that it cannot
' render anything rather than pretend. What it must have NOW is the shape, so
' that deleting the on-device renderer does not leave the project without an
' engine at all -- the system one still works, which is what keeps this commit
' shippable.
'
' Two capability flags deserve the comment they get below. The page really does
' run modern JavaScript: on the server, inside Chromium, where this device can
' neither see it nor invoke it. So SupportsScripting is True and
' InvokeScriptAsync returns nothing, and the shell tells them apart with a type
' test rather than by reading a flag that is describing somebody else's machine.

Imports System.Threading.Tasks
Imports BrowserForWP.Core.Engine

Namespace Engine

    ''' <summary>
    ''' How a navigation ended, in the same shape the shell already handles for the
    ''' WebView, so one handler can report a page from either engine.
    ''' </summary>
    Public NotInheritable Class RemoteNavigationResult

        Public Property IsSuccess As Boolean
        Public Property Url As String

        ''' <summary>A resource key, never a sentence: this layer holds no prose.</summary>
        Public Property StatusKey As String

        ''' <summary>Engine-level detail shown beside the localized reason.</summary>
        Public Property Detail As String

    End Class

    Public NotInheritable Class RemoteEngine
        Implements IBrowserEngine

        Public Event Navigated As EventHandler(Of RemoteNavigationResult)

        Private ReadOnly _capabilities As EngineCapabilities
        Private ReadOnly _host As New Windows.UI.Xaml.Controls.Grid()
        Private _currentUrl As String = String.Empty

        Public Sub New(settings As BrowserForWP.Core.Storage.AppSettings, pinTable As Object)
            _capabilities = New EngineCapabilities()
            _capabilities.Name = "Remote"
            _capabilities.RenderingEngine = "Chromium on a server"
            _capabilities.SupportsTls13 = True

            ' True, and the truth is about the PAGE rather than about this device.
            _capabilities.SupportsScripting = True
            _capabilities.SupportsModernJavaScript = True
            _capabilities.SupportsWebSocket = True
            _capabilities.SupportsFetch = True
        End Sub

        Public ReadOnly Property Capabilities As EngineCapabilities Implements IBrowserEngine.Capabilities
            Get
                Return _capabilities
            End Get
        End Property

        Public ReadOnly Property Source As Object Implements IBrowserEngine.Source
            Get
                Return _host
            End Get
        End Property

        Public Sub Navigate(url As String) Implements IBrowserEngine.Navigate
            _currentUrl = url
        End Sub

        Public Sub GoBack() Implements IBrowserEngine.GoBack
        End Sub

        Public Sub [GoForward]() Implements IBrowserEngine.GoForward
        End Sub

        Public Sub Reload() Implements IBrowserEngine.Reload
        End Sub

        Public Sub [Stop]() Implements IBrowserEngine.Stop
        End Sub

        Public Function InvokeScriptAsync(script As String) As Task(Of String) Implements IBrowserEngine.InvokeScriptAsync
            ' No script host HERE. The page's scripts run inside Chromium on the
            ' server, where this device cannot reach them, and claiming otherwise
            ' is exactly the lie EngineCapabilities exists to prevent.
            Return Task.FromResult(String.Empty)
        End Function

    End Class

End Namespace
