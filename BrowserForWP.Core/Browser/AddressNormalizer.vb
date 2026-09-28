' BrowserForWP — what the user typed, turned into something safe to navigate to.
'
' The security-relevant decision here is that an unrecognised or refused scheme
' becomes a SEARCH, never a navigation. javascript:, data: and file: URLs are
' the classic vectors, and "fail closed" is the only acceptable behaviour for a
' scheme we do not explicitly trust.

Imports System.Text.RegularExpressions

Namespace Browser

    ''' <summary>Where the address bar wants to go, and why.</summary>
    Public NotInheritable Class NavigationTarget

        Public Sub New(url As String, isSearch As Boolean, isValid As Boolean)
            Me.Url = url
            Me.IsSearch = isSearch
            Me.IsValid = isValid
        End Sub

        Public ReadOnly Url As String
        Public ReadOnly IsSearch As Boolean

        ''' <summary>
        ''' False when the input was refused. The URL still holds a safe search
        ''' target, so callers can navigate to it; they must not treat it as the
        ''' address the user asked for.
        ''' </summary>
        Public ReadOnly IsValid As Boolean
    End Class

    ''' <summary>Turns address-bar text into a safe navigation target.</summary>
    Public NotInheritable Class AddressNormalizer

        ''' <summary>
        ''' Search endpoint. Chosen because it is reachable over TLS 1.2, which is
        ''' the ceiling for anything the system WebView loads.
        ''' </summary>
        Private Const SearchPrefix As String = "https://duckduckgo.com/?q="

        ''' <summary>Schemes we will navigate to. Everything else is refused.</summary>
        Private Shared ReadOnly AllowedSchemes As String() = {"http", "https"}

        ''' <summary>
        ''' Host, optional port, optional path/query/fragment.
        '''
        ''' Note the absence of RegexOptions.Compiled: the WinRT profile does not
        ''' support runtime code generation for regexes, so Compiled is at best
        ''' ignored and at worst throws. Interpreted evaluation is correct here and
        ''' the patterns are trivial.
        ''' </summary>
        Private Shared ReadOnly HostWithOptionalParts As New Regex(
            "^[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?)*(\:[0-9]{1,5})?([/?#].*)?$")

        ''' <summary>A hostname: at least one dot, a TLD of 2+ letters.</summary>
        Private Shared ReadOnly DottedHost As New Regex(
            "^([A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,}$")

        Private Sub New()
        End Sub

        Public Shared Function Normalize(input As String) As NavigationTarget
            If input Is Nothing Then input = String.Empty
            Dim text = input.Trim()

            If text.Length = 0 Then
                Return New NavigationTarget(SearchPrefix, True, True)
            End If

            ' Does the text declare a scheme? A colon before any slash means yes.
            Dim colon = text.IndexOf(":"c)
            Dim slash = text.IndexOf("/"c)
            If colon > 0 AndAlso (slash < 0 OrElse colon < slash) Then
                Dim scheme = text.Substring(0, colon).ToLowerInvariant()
                If scheme = "http" OrElse scheme = "https" Then
                    If IsNavigableHttpUrl(text) Then
                        Return New NavigationTarget(text, False, True)
                    End If
                End If
                ' javascript:, file:, data:, ms-appx:, vbscript: and anything else
                ' land here: refused, and degraded to a search.
                Return New NavigationTarget(SearchPrefix & Uri.EscapeDataString(text), True, False)
            End If

            ' A bare host: "example.com", "example.com:8443/x"
            If HostWithOptionalParts.IsMatch(text) Then
                Dim authority = text.Split("/"c, "?"c, "#"c)(0)
                Dim hostOnly = authority.Split(":"c)(0)
                If DottedHost.IsMatch(hostOnly) Then
                    ' Default to https: a user typing a bare hostname wants the
                    ' secure origin, not the legacy one.
                    Return New NavigationTarget("https://" & text, False, True)
                End If
            End If

            ' localhost has no dot but is unambiguous, and is useful in development.
            If text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) Then
                Return New NavigationTarget("http://" & text, False, True)
            End If

            ' Anything else is a search query.
            Return New NavigationTarget(SearchPrefix & Uri.EscapeDataString(text), True, True)
        End Function

        Private Shared Function IsNavigableHttpUrl(text As String) As Boolean
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(text, UriKind.Absolute, uri) Then Return False

            Dim scheme = uri.Scheme.ToLowerInvariant()
            Dim allowed = False
            For Each candidate In AllowedSchemes
                If candidate = scheme Then allowed = True : Exit For
            Next
            If Not allowed Then Return False

            Return Not String.IsNullOrEmpty(uri.Host)
        End Function
    End Class

End Namespace
