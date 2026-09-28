' BrowserForWP — persisted browser settings (pure data holder, no WinRT here).
'
' Persistence lives in the app layer (ApplicationData LocalSettings); this class
' only holds values, defaults and map import/export so it stays unit-testable
' off-device. The Node mirror is tools/proto/useragents.mjs.
'
' Properties are auto-implemented (single line, no Get/Set block): the static
' checker only recognises a parameterless Set, while real VB setters always
' carry a parameter, so an explicit Set block desynchronises its stack.
' Empty-guards live in LoadFromMap and at the call sites instead.

Imports System.Collections.Generic

Namespace Storage

    ''' <summary>All user-tunable browser settings with safe defaults.</summary>
    Public NotInheritable Class AppSettings

        Public Const DefaultHomepage As String = "https://duckduckgo.com/"
        Public Const DefaultSearchTemplate As String = "https://duckduckgo.com/?q={q}"
        Public Const DefaultDohUrl As String = "https://cloudflare-dns.com/dns-query"

        Public Sub New()
            Homepage = DefaultHomepage
            SearchTemplate = DefaultSearchTemplate
            DohUrl = DefaultDohUrl
            DesktopMode = False
            LanguageOverride = Nothing
        End Sub

        Public Property Homepage As String
        Public Property SearchTemplate As String
        Public Property DohUrl As String
        Public Property DesktopMode As Boolean
        Public Property LanguageOverride As String

        ''' <summary>Build a search URL from raw query text.</summary>
        Public Function SearchUrlFor(queryText As String) As String
            Dim safeQuery As String = If(queryText, String.Empty)
            Dim templateText As String = If(String.IsNullOrEmpty(SearchTemplate), DefaultSearchTemplate, SearchTemplate)
            Return templateText.Replace("{q}", Uri.EscapeDataString(safeQuery))
        End Function

        ''' <summary>Export to a plain string map for LocalSettings persistence.</summary>
        Public Function SaveToMap() As Dictionary(Of String, String)
            Dim hostMap As New Dictionary(Of String, String)()
            hostMap("homepage") = If(String.IsNullOrEmpty(Homepage), DefaultHomepage, Homepage)
            hostMap("searchTemplate") = If(String.IsNullOrEmpty(SearchTemplate), DefaultSearchTemplate, SearchTemplate)
            hostMap("dohUrl") = If(String.IsNullOrEmpty(DohUrl), DefaultDohUrl, DohUrl)
            hostMap("desktopMode") = If(DesktopMode, "1", "0")
            hostMap("languageOverride") = If(LanguageOverride, String.Empty)
            Return hostMap
        End Function

        ''' <summary>Import from a plain string map; unknown keys are ignored.</summary>
        Public Sub LoadFromMap(sourceMap As IDictionary(Of String, String))
            If sourceMap Is Nothing Then
                Return
            End If
            Dim foundValue As String = Nothing
            If sourceMap.TryGetValue("homepage", foundValue) Then
                If String.IsNullOrEmpty(foundValue) Then
                    Homepage = DefaultHomepage
                Else
                    Homepage = foundValue
                End If
            End If
            If sourceMap.TryGetValue("searchTemplate", foundValue) Then
                If String.IsNullOrEmpty(foundValue) Then
                    SearchTemplate = DefaultSearchTemplate
                Else
                    SearchTemplate = foundValue
                End If
            End If
            If sourceMap.TryGetValue("dohUrl", foundValue) Then
                If String.IsNullOrEmpty(foundValue) Then
                    DohUrl = DefaultDohUrl
                Else
                    DohUrl = foundValue
                End If
            End If
            If sourceMap.TryGetValue("desktopMode", foundValue) Then
                DesktopMode = (foundValue = "1")
            End If
            If sourceMap.TryGetValue("languageOverride", foundValue) Then
                If String.IsNullOrEmpty(foundValue) Then
                    LanguageOverride = Nothing
                Else
                    LanguageOverride = foundValue
                End If
            End If
        End Sub
    End Class

End Namespace
