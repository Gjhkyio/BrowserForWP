' BrowserForWP — the browser shell.
'
' Responsibilities, and nothing more: host the engine's control, drive navigation
' from the address bar, keep the chrome localized, and expose the diagnostics
' that make the platform's limits visible instead of mysterious.
'
' All decisions that can be made without a UI live in BrowserForWP.Core so they
' stay unit-testable. This file should read as glue.

Imports BrowserForWP.Core.Browser
Imports BrowserForWP.Core.Engine
Imports BrowserForWP.Localization
Imports Windows.Phone.UI.Input
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Input
Imports Windows.UI.Xaml.Navigation

Public NotInheritable Class MainPage
    Inherits Page

    Private ReadOnly _engine As IBrowserEngine = New TridentEngine()
    Private ReadOnly _session As New BrowserSession()

    ''' <summary>Navigation token, so late progress callbacks from an abandoned navigation are ignored.</summary>
    Private _navigationToken As Object

    Protected Overrides Sub OnNavigatedTo(e As NavigationEventArgs)
        MyBase.OnNavigatedTo(e)

        AddHandler HardwareButtons.BackPressed, AddressOf OnHardwareBackPressed

        ' Host the engine.
        ContentHost.Child = DirectCast(_engine.Source, UIElement)
        AddHandler DirectCast(_engine, TridentEngine).View.NavigationStarting, AddressOf OnNavigationStarting
        AddHandler DirectCast(_engine, TridentEngine).View.NavigationCompleted, AddressOf OnNavigationCompleted

        ApplyLocalizedStrings()
        PopulateLanguagePicker()

        _session.NewTab()
        _engine.Navigate("https://duckduckgo.com/")
    End Sub

    Protected Overrides Sub OnNavigatedFrom(e As NavigationEventArgs)
        MyBase.OnNavigatedFrom(e)
        RemoveHandler HardwareButtons.BackPressed, AddressOf OnHardwareBackPressed
    End Sub

    ''' <summary>
    ''' Every user-visible string comes from the resource files, so the chrome
    ''' follows the phone's language with no code change per language.
    ''' </summary>
    Private Sub ApplyLocalizedStrings()
        AddressBox.PlaceholderText = Localizer.Get("AddressPlaceholder")
        GoButton.Content = Localizer.Get("Go")
        BackButton.Content = Localizer.Get("Back")
        ForwardButton.Content = Localizer.Get("Forward")
        ReloadButton.Content = Localizer.Get("Reload")
        TabsButton.Content = Localizer.Get("NewTab")
        SettingsButton.Content = Localizer.Get("Settings")

        SettingsTitle.Text = Localizer.Get("DiagnosticsTitle")
        LanguageLabel.Text = Localizer.Get("LanguageLabel")
        DiagnosticsTitle.Text = Localizer.Get("DiagnosticsEngineLabel")
        CompatProbeButton.Content = Localizer.Get("DiagnosticsCompatProbe")
        CloseSettingsButton.Content = Localizer.Get("DiagnosticsClose")

        Dim capabilities = _engine.Capabilities
        EngineText.Text = capabilities.ToString() & vbCrLf &
                          Localizer.Get("DiagnosticsProbe") & ": " &
                          If(capabilities.NeedsPolyfillLayer, "compatibility layer active", "native")
    End Sub

    ''' <summary>
    ''' The language list offers Automatic plus each supported language. Automatic
    ''' is first and is the default, which is what makes the app follow the phone.
    ''' </summary>
    Private Sub PopulateLanguagePicker()
        LanguagePicker.Items.Clear()
        LanguagePicker.Items.Add(Localizer.Get("LanguageAutomatic"))
        For Each tag In LanguageCatalog.Supported
            LanguagePicker.Items.Add(LanguageCatalog.DisplayName(tag))
        Next
        LanguagePicker.SelectedIndex = If(Localizer.IsOverridden, 1, 0)
    End Sub

    Private Sub LanguagePicker_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        ' Index 0 is Automatic; anything else maps to a supported tag.
        If LanguagePicker.SelectedIndex <= 0 Then
            Localizer.Override(Nothing)
        ElseIf LanguagePicker.SelectedIndex - 1 < LanguageCatalog.Supported.Length Then
            Localizer.Override(LanguageCatalog.Supported(LanguagePicker.SelectedIndex - 1))
        End If
        ApplyLocalizedStrings()
        PopulateLanguagePicker()
    End Sub

    ' ── Address bar ───────────────────────────────────────────────────────

    Private Sub AddressBox_KeyDown(sender As Object, e As KeyRoutedEventArgs)
        If e.Key <> Windows.System.VirtualKey.Enter Then Return
        NavigateFromAddressBar()
    End Sub

    Private Sub GoButton_Click(sender As Object, e As RoutedEventArgs)
        NavigateFromAddressBar()
    End Sub

    Private Sub NavigateFromAddressBar()
        Dim target = AddressNormalizer.Normalize(AddressBox.Text)

        If Not target.IsValid Then
            ' Fail closed: the input declared a scheme we refuse. Say so rather
            ' than silently navigating somewhere the user did not ask for.
            AddressBox.Text = Localizer.Get("ErrorUnknownScheme")
            Return
        End If

        _session.ActiveTab.PushHistory(target.Url)
        _engine.Navigate(target.Url)
    End Sub

    Private Sub AddressBox_GotFocus(sender As Object, e As RoutedEventArgs)
        AddressBox.SelectAll()
    End Sub

    Private Sub AddressBox_LostFocus(sender As Object, e As RoutedEventArgs)
        ' Restore the real URL after an edit is abandoned.
        AddressBox.Text = _session.ActiveTab.Url
    End Sub

    ' ── Navigation commands ───────────────────────────────────────────────

    Private Sub BackButton_Click(sender As Object, e As RoutedEventArgs)
        ' Drive back/forward through Core so the history model stays the single
        ' source of truth, and mirror the move onto the engine.
        If _session.ActiveTab.CanGoBack Then
            _session.ActiveTab.Back()
            _engine.GoBack()
        End If
    End Sub

    Private Sub ForwardButton_Click(sender As Object, e As RoutedEventArgs)
        If _session.ActiveTab.CanGoForward Then
            _session.ActiveTab.Forward()
            _engine.GoForward()
        End If
    End Sub

    Private Sub ReloadButton_Click(sender As Object, e As RoutedEventArgs)
        _engine.Reload()
    End Sub

    Private Sub TabsButton_Click(sender As Object, e As RoutedEventArgs)
        _session.NewTab()
        _engine.Navigate("https://duckduckgo.com/")
    End Sub

    Private Sub OnHardwareBackPressed(sender As Object, e As BackPressedEventArgs)
        ' The hardware Back key should walk history before it leaves the app.
        If _session.ActiveTab.CanGoBack Then
            _session.ActiveTab.Back()
            _engine.GoBack()
            e.Handled = True
        End If
    End Sub

    ' ── Engine events ─────────────────────────────────────────────────────

    Private Sub OnNavigationStarting(sender As WebView, e As WebViewNavigationStartingEventArgs)
        _navigationToken = e.Uri
        LoadProgress.Value = 0
    End Sub

    Private Sub OnNavigationCompleted(sender As WebView, e As WebViewNavigationCompletedEventArgs)
        ' Ignore callbacks from a navigation the user already abandoned.
        If _navigationToken Is Nothing Then Return
        _navigationToken = Nothing

        LoadProgress.Value = 100
        Dim url = _engine.Source.ToString()
        _session.ActiveTab.ReplaceCurrent(url)
        AddressBox.Text = _session.ActiveTab.Url
        UpdateSecurityGlyph()
    End Sub

    ''' <summary>
    ''' The security indicator reflects what the engine can actually guarantee,
    ''' not what we would like it to. Trident's traffic never uses TLS 1.3, so a
    ''' padlock here means "TLS 1.2 at best".
    ''' </summary>
    Private Sub UpdateSecurityGlyph()
        Dim url = _session.ActiveTab.Url
        Dim isHttps = url IsNot Nothing AndAlso url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)

        If isHttps Then
            SecurityGlyph.Text = If(_engine.Capabilities.SupportsTls13, ChrW(&H1F512), ChrW(&H1F513))
        Else
            SecurityGlyph.Text = ChrW(&H26A0)   ' warning
        End If
    End Sub

    ' ── Diagnostics ───────────────────────────────────────────────────────

    Private Sub SettingsButton_Click(sender As Object, e As RoutedEventArgs)
        SettingsOverlay.Visibility = Visibility.Visible
    End Sub

    Private Sub CloseSettingsButton_Click(sender As Object, e As RoutedEventArgs)
        SettingsOverlay.Visibility = Visibility.Collapsed
    End Sub

    ''' <summary>
    ''' Ask the engine which modern web features it lacks. Without this, a site
    ''' that fails looks identical to a site that is broken — with it, the user
    ''' learns which capability is missing.
    ''' </summary>
    Private Async Sub CompatProbeButton_Click(sender As Object, e As RoutedEventArgs)
        CompatProbeButton.IsEnabled = False
        Try
            Dim probe As New Core.Diagnostics.CompatibilityProbe()
            Dim report = Await probe.RunAsync(_engine)

            If report.IsFullyCompatible Then
                CompatProbeResult.Text = Localizer.Get("DiagnosticsNoMissingFeatures")
            Else
                CompatProbeResult.Text = String.Join(", ", report.MissingFeatures)
            End If
        Catch ex As Exception
            CompatProbeResult.Text = Localizer.Get("ErrorPageFailed")
        Finally
            CompatProbeButton.IsEnabled = True
        End Try
    End Sub
End Class
