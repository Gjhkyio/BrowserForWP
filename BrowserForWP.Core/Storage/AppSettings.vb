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
'
' Lite-first: defaults point at DuckDuckGo Lite, which Trident renders fast.
' Stored Google/Bing templates from earlier versions migrate to the default.

Imports System.Collections.Generic

Namespace Storage

    ''' <summary>All user-tunable browser settings with safe defaults.</summary>
    Public NotInheritable Class AppSettings

        Public Const DefaultHomepage As String = "https://lite.duckduckgo.com/lite/"
        Public Const DefaultSearchTemplate As String = "https://lite.duckduckgo.com/lite/?q={q}"
        Public Const DefaultDohUrl As String = "https://cloudflare-dns.com/dns-query"

        ''' <summary>Maximum tabs kept across a session restore (speed + memory).</summary>
        Public Const MaxSessionTabs As Integer = 10

        Public Sub New()
            Homepage = DefaultHomepage
            SearchTemplate = DefaultSearchTemplate
            DohUrl = DefaultDohUrl
            DesktopMode = False
            LanguageOverride = Nothing
            NightMode = False
            BlockTrackers = True
            RestoreSession = False
            LiteRedirects = True
            EngineSetting = Engine.EngineChoice.Auto
            LastSessionTabs = String.Empty
        End Sub

        Public Property Homepage As String
        Public Property SearchTemplate As String
        Public Property DohUrl As String
        Public Property DesktopMode As Boolean
        Public Property LanguageOverride As String
        Public Property NightMode As Boolean
        Public Property BlockTrackers As Boolean
        Public Property RestoreSession As Boolean
        Public Property LiteRedirects As Boolean

        ''' <summary>
        ''' Which engine renders, as one of Engine.EngineChoice's three keywords.
        '''
        ''' Named EngineSetting rather than EngineChoice on purpose: VB is
        ''' case-insensitive, so a property called EngineChoice would shadow the type
        ''' of the same name inside this class and every use of EngineChoice.Auto
        ''' below it would become a reference to a String.
        '''
        ''' Auto is the default, which means "the system engine unless a measurement
        ''' says otherwise": upgrading this app must not change what a user sees
        ''' without being asked.
        ''' </summary>
        Public Property EngineSetting As String

        Public Property LastSessionTabs As String

        ''' <summary>Build a search URL from raw query text.</summary>
        Public Function SearchUrlFor(queryText As String) As String
            Dim safeQuery As String = If(queryText, String.Empty)
            Dim templateText As String = If(String.IsNullOrEmpty(SearchTemplate), DefaultSearchTemplate, SearchTemplate)
            Return templateText.Replace("{q}", Uri.EscapeDataString(safeQuery))
        End Function

        ''' <summary>Tabs saved for restore, http(s) only, capped.</summary>
        Public Function GetSessionTabs() As List(Of String)
            Dim result As New List(Of String)()
            If String.IsNullOrEmpty(LastSessionTabs) Then
                Return result
            End If
            Dim rawLines As String() = LastSessionTabs.Split(New String() {vbLf}, StringSplitOptions.None)
            For Each rawLine In rawLines
                Dim cleanLine As String = rawLine.Trim()
                If cleanLine.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                    result.Add(cleanLine)
                End If
                If result.Count >= MaxSessionTabs Then
                    Exit For
                End If
            Next
            Return result
        End Function

        ''' <summary>Store tabs for restore: first 10, each truncated to 300 chars.</summary>
        Public Sub SetSessionTabs(pageUrls As IList(Of String))
            Dim kept As New List(Of String)()
            If pageUrls IsNot Nothing Then
                For Each pageUrl In pageUrls
                    If String.IsNullOrEmpty(pageUrl) Then
                        Continue For
                    End If
                    Dim cleanUrl As String = pageUrl.Trim()
                    If cleanUrl.Length > 300 Then
                        cleanUrl = cleanUrl.Substring(0, 300)
                    End If
                    kept.Add(cleanUrl)
                    If kept.Count >= MaxSessionTabs Then
                        Exit For
                    End If
                Next
            End If
            LastSessionTabs = String.Join(vbLf, kept.ToArray())
        End Sub

        ''' <summary>Legacy heavy search engines migrate to the lite default.</summary>
        Public Shared Function MigrateSearchTemplate(storedTemplate As String) As String
            If String.IsNullOrEmpty(storedTemplate) Then
                Return DefaultSearchTemplate
            End If
            Dim loweredTemplate As String = storedTemplate.ToLowerInvariant()
            If loweredTemplate.Contains("google.") OrElse loweredTemplate.Contains("bing.") Then
                Return DefaultSearchTemplate
            End If
            Return storedTemplate
        End Function

        ''' <summary>Export to a plain string map for LocalSettings persistence.</summary>
        Public Function SaveToMap() As Dictionary(Of String, String)
            Dim hostMap As New Dictionary(Of String, String)()
            hostMap("homepage") = If(String.IsNullOrEmpty(Homepage), DefaultHomepage, Homepage)
            hostMap("searchTemplate") = If(String.IsNullOrEmpty(SearchTemplate), DefaultSearchTemplate, SearchTemplate)
            hostMap("dohUrl") = If(String.IsNullOrEmpty(DohUrl), DefaultDohUrl, DohUrl)
            hostMap("desktopMode") = If(DesktopMode, "1", "0")
            hostMap("languageOverride") = If(LanguageOverride, String.Empty)
            hostMap("nightMode") = If(NightMode, "1", "0")
            hostMap("blockTrackers") = If(BlockTrackers, "1", "0")
            hostMap("restoreSession") = If(RestoreSession, "1", "0")
            hostMap("liteRedirects") = If(LiteRedirects, "1", "0")
            hostMap("engineSetting") = If(String.IsNullOrEmpty(EngineSetting), Engine.EngineChoice.Auto, EngineSetting)
            hostMap("lastSessionTabs") = If(LastSessionTabs, String.Empty)
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
                SearchTemplate = MigrateSearchTemplate(foundValue)
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
            If sourceMap.TryGetValue("nightMode", foundValue) Then
                NightMode = (foundValue = "1")
            End If
            If sourceMap.TryGetValue("blockTrackers", foundValue) Then
                If String.IsNullOrEmpty(foundValue) Then
                    BlockTrackers = True
                Else
                    BlockTrackers = (foundValue = "1")
                End If
            End If
            If sourceMap.TryGetValue("restoreSession", foundValue) Then
                RestoreSession = (foundValue = "1")
            End If
            If sourceMap.TryGetValue("liteRedirects", foundValue) Then
                If String.IsNullOrEmpty(foundValue) Then
                    LiteRedirects = True
                Else
                    LiteRedirects = (foundValue = "1")
                End If
            End If
            If sourceMap.TryGetValue("engineSetting", foundValue) Then
                ' Normalize, not a raw copy: a stored value this version does not
                ' recognise becomes Auto instead of leaving the shell with an engine
                ' keyword nothing understands.
                EngineSetting = Engine.EngineChoice.Normalize(foundValue)
            End If
            If sourceMap.TryGetValue("lastSessionTabs", foundValue) Then
                LastSessionTabs = If(foundValue, String.Empty)
            End If
        End Sub
    End Class

End Namespace
