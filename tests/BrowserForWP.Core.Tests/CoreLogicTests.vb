' BrowserForWP — Core logic checks (build-safe, no MSTest dependency).
'
' Runs in the guest as a library: call CoreLogicTests.RunAll() from Test
' Explorer or a debug page. Every Check throws InvalidOperationException on
' failure so a regression is loud. Node mirrors cover the same behavior
' off-device (tools/proto/useragents.mjs, pinstore.mjs).

Imports BrowserForWP.Core.Browser
Imports BrowserForWP.Core.Storage
Imports BrowserForWP.Localization
Imports BrowserForWP.Net.Tls13

Namespace CoreTests

    ''' <summary>Pure-logic regression checks for Core + pin/hostname helpers.</summary>
    Public NotInheritable Class CoreLogicTests

        Private Sub New()
        End Sub

        Private Shared Sub Check(ok As Boolean, checkName As String)
            If Not ok Then
                Throw New InvalidOperationException("check failed: " & checkName)
            End If
        End Sub

        Public Shared Function RunAll() As Integer
            Dim ran As Integer = 0

            Dim searchTarget = AddressNormalizer.Normalize("hello world")
            Check(searchTarget.IsSearch, "search classified")
            ran += 1

            Dim urlTarget = AddressNormalizer.Normalize("example.com")
            Check(urlTarget.Url = "https://example.com", "bare host https")
            ran += 1

            Dim refusedTarget = AddressNormalizer.Normalize("javascript:alert(1)")
            Check(Not refusedTarget.IsValid, "javascript refused")
            ran += 1

            Dim tab As New TabModel()
            tab.PushHistory("https://a.example/")
            tab.PushHistory("https://b.example/")
            Check(tab.CanGoBack, "tab can go back")
            tab.Back()
            Check(tab.Url = "https://a.example/", "tab back url")
            ran += 1

            Dim session As New BrowserSession()
            session.NewTab()
            Check(session.ActiveTab IsNot Nothing, "session active tab")
            Check(session.EffectiveUserAgent = UserAgents.MobileDefault, "default UA mobile")
            session.DesktopMode = True
            Check(session.EffectiveUserAgent = UserAgents.DesktopWindows, "desktop UA")
            ran += 1

            Check(UserAgents.EffectiveUserAgent(False) = UserAgents.MobileDefault, "UA helper mobile")
            ran += 1

            Dim appSettings As New AppSettings()
            ' The default template is the LITE endpoint, so the expected value is the
            ' lite URL. This assertion previously named the heavy duckduckgo.com
            ' endpoint that the lite-first default replaced; it compiled, so only
            ' running it would have caught the drift.
            Check(appSettings.SearchUrlFor("hello world") = "https://lite.duckduckgo.com/lite/?q=hello%20world", "search url")
            Check(appSettings.SearchUrlFor("hello world").StartsWith("https://lite.duckduckgo.com/"), "search default is lite")
            Dim hostMap As Dictionary(Of String, String) = appSettings.SaveToMap()
            Dim reloaded As New AppSettings()
            reloaded.LoadFromMap(hostMap)
            Check(reloaded.Homepage = appSettings.Homepage, "settings roundtrip")
            ran += 1

            Dim history As New HistoryStore()
            history.Add("https://a.example/", "A")
            Dim savedHistory As String = history.Serialize()
            Dim history2 As New HistoryStore()
            history2.Parse(savedHistory)
            Check(history2.Count = 1, "history roundtrip")
            ran += 1

            Dim favorites As New FavoritesStore()
            Check(favorites.Add("https://a.example/", "A"), "fav add")
            Check(Not favorites.Add("https://a.example/", "A"), "fav dup rejected")
            Check(favorites.Contains("https://a.example/"), "fav contains")
            ran += 1

            Check(PinStore.NormalizeHost("Example.COM:443") = "example.com", "pin host normalize")
            Dim pins As New PinStore()
            pins.Add("Example.com", "abc123")
            Check(pins.Verify("example.com", "abc123"), "pin match")
            Check(Not pins.Verify("example.com", "zzz"), "pin mismatch")
            Check(pins.Verify("other.com", Nothing), "no pin passes")
            ran += 1

            Dim hostNames As New List(Of String)()
            hostNames.Add("*.example.com")
            Dim wildHit = CertificateValidator.MatchHostname(hostNames, "a.example.com")
            Check(wildHit.IsMatch, "wildcard one label")
            Dim wildDeep = CertificateValidator.MatchHostname(hostNames, "a.b.example.com")
            Check(Not wildDeep.IsMatch, "wildcard not two labels")
            ran += 1

            Check(LanguageCatalog.Match(New String() {"it-IT"}) = "it-IT", "lang it")
            Check(LanguageCatalog.Match(New String() {"xx"}) = "en-US", "lang fallback")
            ran += 1

            Return ran
        End Function
    End Class

End Namespace
