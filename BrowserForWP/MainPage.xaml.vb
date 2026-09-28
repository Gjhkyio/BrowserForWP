' BrowserForWP — the browser shell.
'
' Hosts the engine, drives navigation, persists settings/history/favorites/pins,
' injects the compat bundle, and exposes TLS-probe + compatibility diagnostics.

Imports BrowserForWP.Core.Browser
Imports BrowserForWP.Core.Diagnostics
Imports BrowserForWP.Core.Engine
Imports BrowserForWP.Core.Engine.Native
Imports BrowserForWP.Core.Storage
Imports BrowserForWP.Localization
Imports BrowserForWP.Net.Tls13
Imports Windows.ApplicationModel.DataTransfer
Imports Windows.Phone.UI.Input
Imports Windows.Storage
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Input
Imports Windows.UI.Xaml.Navigation
Imports Windows.Web

Public NotInheritable Class MainPage
    Inherits Page

    Private ReadOnly _engine As IBrowserEngine = New TridentEngine()
    Private ReadOnly _session As New BrowserSession()
    Private ReadOnly _appSettings As New AppSettings()
    Private ReadOnly _historyStore As New HistoryStore()
    Private ReadOnly _favoritesStore As New FavoritesStore()
    Private ReadOnly _pinTable As New PinStore()

    Private _navigationToken As Object
    Private _searchTemplates As String()

    ''' <summary>True while pickers/lists are repopulated, so programmatic selection is ignored.</summary>
    Private _populatingLanguage As Boolean = False
    Private _refreshingTabs As Boolean = False

    Protected Overrides Sub OnNavigatedTo(e As NavigationEventArgs)
        MyBase.OnNavigatedTo(e)

        AddHandler HardwareButtons.BackPressed, AddressOf OnHardwareBackPressed
        AddHandler DataTransferManager.GetForCurrentView().DataRequested, AddressOf OnShareRequested

        ContentHost.Child = DirectCast(_engine.Source, UIElement)
        Dim tridentView As TridentEngine = DirectCast(_engine, TridentEngine)
        AddHandler tridentView.View.NavigationStarting, AddressOf OnNavigationStarting
        AddHandler tridentView.View.DOMContentLoaded, AddressOf OnDOMContentLoaded
        AddHandler tridentView.View.NavigationCompleted, AddressOf OnNavigationCompleted
        ' No NavigationFailed handler: that event is deprecated on Windows Phone
        ' 8.1 and carries no URI. Completion reports the same failure through
        ' IsSuccess / WebErrorStatus, so failures are handled there instead.

        _searchTemplates = New String() {
            "https://duckduckgo.com/?q={q}",
            "https://lite.duckduckgo.com/lite/?q={q}"
        }

        LoadPersistedState()
        ApplyLocalizedStrings()
        PopulateLanguagePicker()
        PopulateSearchEnginePicker()
        RefreshTabsList()
        RefreshHistoryList()
        RefreshFavoritesList()

        _session.DesktopMode = _appSettings.DesktopMode
        Dim restoredTabs As List(Of String) = _appSettings.GetSessionTabs()
        If _appSettings.RestoreSession AndAlso restoredTabs.Count > 0 Then
            For Each restoredUrl In restoredTabs
                _session.NewTab()
                _session.ActiveTab.PushHistory(restoredUrl)
            Next
            _session.ActivateTab(_session.Tabs.Count - 1)
            _engine.Navigate(_session.ActiveTab.Url)
        Else
            _session.NewTab()
            _engine.Navigate(_appSettings.Homepage)
        End If
    End Sub

    Protected Overrides Sub OnNavigatedFrom(e As NavigationEventArgs)
        MyBase.OnNavigatedFrom(e)
        RemoveHandler HardwareButtons.BackPressed, AddressOf OnHardwareBackPressed
        RemoveHandler DataTransferManager.GetForCurrentView().DataRequested, AddressOf OnShareRequested
        Try
            RemoveHandler DirectCast(_engine, TridentEngine).View.DOMContentLoaded, AddressOf OnDOMContentLoaded
        Catch ex As Exception
        End Try
        SavePersistedState()
    End Sub

    Private Sub LoadPersistedState()
        Try
            Dim localValues = ApplicationData.Current.LocalSettings.Values
            Dim hostMap As New Dictionary(Of String, String)()
            For Each pairItem In localValues
                If TypeOf pairItem.Value Is String Then
                    hostMap(pairItem.Key) = DirectCast(pairItem.Value, String)
                End If
            Next
            _appSettings.LoadFromMap(hostMap)
            If localValues.ContainsKey("history") Then
                Dim historyText As String = TryCast(localValues("history"), String)
                _historyStore.Parse(If(historyText, String.Empty))
            End If
            If localValues.ContainsKey("favorites") Then
                Dim favoritesText As String = TryCast(localValues("favorites"), String)
                _favoritesStore.Parse(If(favoritesText, String.Empty))
            End If
            If localValues.ContainsKey("pins") Then
                Dim pinsText As String = TryCast(localValues("pins"), String)
                _pinTable.Parse(If(pinsText, String.Empty))
            End If
            If Not String.IsNullOrEmpty(_appSettings.LanguageOverride) Then
                Localizer.Override(_appSettings.LanguageOverride)
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub SavePersistedState()
        Try
            Dim localValues = ApplicationData.Current.LocalSettings.Values
            Dim hostMap As Dictionary(Of String, String) = _appSettings.SaveToMap()
            For Each pairItem In hostMap
                localValues(pairItem.Key) = pairItem.Value
            Next
            localValues("history") = _historyStore.Serialize()
            localValues("favorites") = _favoritesStore.Serialize()
            localValues("pins") = _pinTable.Serialize()
        Catch ex As Exception
        End Try
    End Sub

    Private Sub ApplyLocalizedStrings()
        AddressBox.PlaceholderText = Localizer.Get("AddressPlaceholder")
        GoButton.Content = Localizer.Get("Go")
        BackButton.Content = Localizer.Get("Back")
        ForwardButton.Content = Localizer.Get("Forward")
        ReloadButton.Content = Localizer.Get("Reload")
        StopButton.Content = Localizer.Get("Stop")
        TabsButton.Content = Localizer.Get("NewTab") & " (" & _session.Tabs.Count & ")"
        FindButton.Content = Localizer.Get("Find")
        ReadingButton.Content = Localizer.Get("ReadingMode")
        ShareButton.Content = Localizer.Get("Share")
        FindNextButton.Content = Localizer.Get("Find")
        FindCloseButton.Content = Localizer.Get("DiagnosticsClose")
        SecurityDetailsButton.Content = Localizer.Get("SecurityDetails")
        SettingsButton.Content = Localizer.Get("Settings")

        ' The Settings overlay is titled "Settings". It used to read "Diagnostics",
        ' because these two heading keys were swapped with the ones below.
        SettingsTitle.Text = Localizer.Get("Settings")
        LanguageLabel.Text = Localizer.Get("LanguageLabel")
        DiagnosticsTitle.Text = Localizer.Get("DiagnosticsTitle")
        CompatProbeButton.Content = Localizer.Get("DiagnosticsCompatProbe")
        CloseSettingsButton.Content = Localizer.Get("DiagnosticsClose")
        DiagnosticsButton.Content = Localizer.Get("Diagnostics")
        DiagnosticsBackButton.Content = Localizer.Get("Back")
        DesktopToggle.Content = Localizer.Get("DesktopSite")
        PrivateModeToggle.Content = Localizer.Get("PrivateMode")
        NightModeToggle.Content = Localizer.Get("NightMode")
        BlockTrackersToggle.Content = Localizer.Get("BlockTrackers")
        RestoreSessionToggle.Content = Localizer.Get("RestoreSession")
        LiteRedirectsToggle.Content = Localizer.Get("LiteRedirects")
        HomepageLabel.Text = Localizer.Get("HomepageLabel")
        SearchEngineLabel.Text = Localizer.Get("SearchEngineLabel")
        TabsTitle.Text = Localizer.Get("TabsTitle")
        CloseTabButton.Content = Localizer.Get("CloseTab")
        HistoryTitle.Text = Localizer.Get("HistoryTitle")
        ClearHistoryButton.Content = Localizer.Get("ClearHistory")
        FavoritesTitle.Text = Localizer.Get("FavoritesTitle")
        AddFavoriteButton.Content = Localizer.Get("AddFavorite")
        RemoveFavoriteButton.Content = Localizer.Get("RemoveFavorite")
        TlsProbeHostLabel.Text = Localizer.Get("TlsProbeHostLabel")
        TlsProbeButton.Content = Localizer.Get("TlsProbeRun")
        DohServerLabel.Text = Localizer.Get("DohServerLabel")
        PinTitle.Text = Localizer.Get("PinTitle")
        PinAddButton.Content = Localizer.Get("PinAdd")
        PinRemoveButton.Content = Localizer.Get("PinRemove")
        ParsePageButton.Content = Localizer.Get("ParseThisPage")
        RenderNativeButton.Content = Localizer.Get("RenderNatively")
        IeModeButton.Content = Localizer.Get("IeModeCheck")

        DesktopToggle.IsChecked = _session.DesktopMode
        PrivateModeToggle.IsChecked = _session.PrivateMode
        NightModeToggle.IsChecked = _appSettings.NightMode
        BlockTrackersToggle.IsChecked = _appSettings.BlockTrackers
        RestoreSessionToggle.IsChecked = _appSettings.RestoreSession
        LiteRedirectsToggle.IsChecked = _appSettings.LiteRedirects
        HomepageBox.Text = _appSettings.Homepage
        DohServerBox.Text = _appSettings.DohUrl

        Dim capabilities = _engine.Capabilities
        ' The layer state used to be hardcoded English ("compatibility layer
        ' active") and was prefixed with DiagnosticsProbe, which is the TLS probe
        ' button's own label ("Run TLS probe"). Both now come from the catalogue.
        Dim layerState As String = If(capabilities.NeedsPolyfillLayer,
                                      Localizer.Get("CompatLayerActive"),
                                      Localizer.Get("CompatLayerNative"))
        EngineText.Text = Localizer.Get("DiagnosticsEngineLabel") & ":" & vbCrLf &
                          capabilities.ToString() & vbCrLf &
                          Localizer.Get("CompatLayerLabel") & ": " & layerState
    End Sub

    Private Sub PopulateLanguagePicker()
        _populatingLanguage = True
        Try
            LanguagePicker.Items.Clear()
            LanguagePicker.Items.Add(Localizer.Get("LanguageAutomatic"))
            For Each supportedTag As String In LanguageCatalog.Supported
                LanguagePicker.Items.Add(LanguageCatalog.DisplayName(supportedTag))
            Next
            LanguagePicker.SelectedIndex = If(Localizer.IsOverridden, 1, 0)
        Finally
            _populatingLanguage = False
        End Try
    End Sub

    Private Sub PopulateSearchEnginePicker()
        SearchEnginePicker.Items.Clear()
        SearchEnginePicker.Items.Add("DuckDuckGo")
        SearchEnginePicker.Items.Add("DuckDuckGo Lite")
        Dim currentTemplate As String = _appSettings.SearchTemplate
        Dim pickedIndex As Integer = 0
        For i As Integer = 0 To _searchTemplates.Length - 1
            If _searchTemplates(i) = currentTemplate Then
                pickedIndex = i
                Exit For
            End If
        Next
        SearchEnginePicker.SelectedIndex = pickedIndex
    End Sub

    Private Sub RefreshTabsList()
        _refreshingTabs = True
        Try
            TabsList.Items.Clear()
            For i As Integer = 0 To _session.Tabs.Count - 1
                Dim tabUrl As String = _session.Tabs(i).Url
                If String.IsNullOrEmpty(tabUrl) Then
                    tabUrl = _appSettings.Homepage
                End If
                TabsList.Items.Add((i + 1) & ": " & tabUrl)
            Next
            If _session.ActiveIndex >= 0 AndAlso _session.ActiveIndex < TabsList.Items.Count Then
                TabsList.SelectedIndex = _session.ActiveIndex
            End If
            TabsButton.Content = Localizer.Get("NewTab") & " (" & _session.Tabs.Count & ")"
        Finally
            _refreshingTabs = False
        End Try
    End Sub

    Private Sub RefreshHistoryList()
        HistoryList.Items.Clear()
        Dim entries As IList(Of HistoryEntry) = _historyStore.List()
        For i As Integer = entries.Count - 1 To 0 Step -1
            HistoryList.Items.Add(entries(i).Url)
            If HistoryList.Items.Count >= 50 Then
                Exit For
            End If
        Next
    End Sub

    Private Sub RefreshFavoritesList()
        FavoritesList.Items.Clear()
        Dim entries As IList(Of FavoriteEntry) = _favoritesStore.List()
        For Each favEntry In entries
            FavoritesList.Items.Add(favEntry.Url)
        Next
    End Sub

    Private Sub LanguagePicker_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If _populatingLanguage Then
            Return
        End If
        If LanguagePicker.SelectedIndex <= 0 Then
            Localizer.Override(Nothing)
            _appSettings.LanguageOverride = Nothing
        ElseIf LanguagePicker.SelectedIndex - 1 < LanguageCatalog.Supported.Length Then
            Dim pickedTag As String = LanguageCatalog.Supported(LanguagePicker.SelectedIndex - 1)
            Localizer.Override(pickedTag)
            _appSettings.LanguageOverride = pickedTag
        End If
        SavePersistedState()
        ApplyLocalizedStrings()
        PopulateLanguagePicker()
    End Sub

    Private Sub DesktopToggle_Checked(sender As Object, e As RoutedEventArgs)
        _session.DesktopMode = True
        _appSettings.DesktopMode = True
        SavePersistedState()
    End Sub

    Private Sub DesktopToggle_Unchecked(sender As Object, e As RoutedEventArgs)
        _session.DesktopMode = False
        _appSettings.DesktopMode = False
        SavePersistedState()
    End Sub

    Private Sub PrivateModeToggle_Checked(sender As Object, e As RoutedEventArgs)
        _session.PrivateMode = True
    End Sub

    Private Sub PrivateModeToggle_Unchecked(sender As Object, e As RoutedEventArgs)
        _session.PrivateMode = False
    End Sub

    Private Sub NightModeToggle_Checked(sender As Object, e As RoutedEventArgs)
        _appSettings.NightMode = True
        SavePersistedState()
        ApplyNightMode()
    End Sub

    Private Sub NightModeToggle_Unchecked(sender As Object, e As RoutedEventArgs)
        _appSettings.NightMode = False
        SavePersistedState()
        ApplyNightMode()
    End Sub

    Private Sub BlockTrackersToggle_Checked(sender As Object, e As RoutedEventArgs)
        _appSettings.BlockTrackers = True
        SavePersistedState()
    End Sub

    Private Sub BlockTrackersToggle_Unchecked(sender As Object, e As RoutedEventArgs)
        _appSettings.BlockTrackers = False
        SavePersistedState()
    End Sub

    Private Sub RestoreSessionToggle_Checked(sender As Object, e As RoutedEventArgs)
        _appSettings.RestoreSession = True
        SavePersistedState()
    End Sub

    Private Sub RestoreSessionToggle_Unchecked(sender As Object, e As RoutedEventArgs)
        _appSettings.RestoreSession = False
        SavePersistedState()
    End Sub

    Private Sub LiteRedirectsToggle_Checked(sender As Object, e As RoutedEventArgs)
        _appSettings.LiteRedirects = True
        SavePersistedState()
    End Sub

    Private Sub LiteRedirectsToggle_Unchecked(sender As Object, e As RoutedEventArgs)
        _appSettings.LiteRedirects = False
        SavePersistedState()
    End Sub

    Private Async Sub ApplyNightMode()
        Try
            Await DirectCast(_engine, TridentEngine).SetNightModeAsync(_appSettings.NightMode)
        Catch ex As Exception
        End Try
    End Sub

    Private Sub HomepageBox_LostFocus(sender As Object, e As RoutedEventArgs)
        Dim typedHome As String = HomepageBox.Text.Trim()
        If Not String.IsNullOrEmpty(typedHome) Then
            _appSettings.Homepage = typedHome
            SavePersistedState()
        End If
    End Sub

    Private Sub SearchEnginePicker_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim picked As Integer = SearchEnginePicker.SelectedIndex
        If picked >= 0 AndAlso picked < _searchTemplates.Length Then
            _appSettings.SearchTemplate = _searchTemplates(picked)
            SavePersistedState()
        End If
    End Sub

    Private Sub DohServerBox_LostFocus(sender As Object, e As RoutedEventArgs)
        Dim typedDoh As String = DohServerBox.Text.Trim()
        If Not String.IsNullOrEmpty(typedDoh) Then
            _appSettings.DohUrl = typedDoh
            SavePersistedState()
        End If
    End Sub

    Private Sub AddressBox_KeyDown(sender As Object, e As KeyRoutedEventArgs)
        If e.Key <> Windows.System.VirtualKey.Enter Then Return
        NavigateFromAddressBar()
    End Sub

    Private Sub GoButton_Click(sender As Object, e As RoutedEventArgs)
        NavigateFromAddressBar()
    End Sub

    Private Sub NavigateFromAddressBar()
        HideError()
        Dim rawText As String = AddressBox.Text.Trim()
        Dim target = AddressNormalizer.Normalize(rawText)

        If Not target.IsValid Then
            AddressBox.Text = Localizer.Get("ErrorUnknownScheme")
            Return
        End If

        Dim destUrl As String = target.Url
        If target.IsSearch Then
            destUrl = _appSettings.SearchUrlFor(rawText)
        End If

        If _appSettings.LiteRedirects Then
            Dim liteUrl As String = LiteRedirects.RedirectUrl(destUrl)
            If Not String.IsNullOrEmpty(liteUrl) Then
                destUrl = liteUrl
            End If
        End If

        If IsBlockedTrackerUrl(destUrl) Then
            ShowBlockedTracker()
            Return
        End If

        _session.ActiveTab.PushHistory(destUrl)
        RefreshTabsList()
        _engine.Navigate(destUrl)
    End Sub

    Private Sub AddressBox_GotFocus(sender As Object, e As RoutedEventArgs)
        AddressBox.SelectAll()
    End Sub

    Private Sub AddressBox_LostFocus(sender As Object, e As RoutedEventArgs)
        AddressBox.Text = _session.ActiveTab.Url
    End Sub

    Private Sub BackButton_Click(sender As Object, e As RoutedEventArgs)
        If _session.ActiveTab.CanGoBack Then
            _session.ActiveTab.Back()
            _engine.GoBack()
            RefreshTabsList()
        End If
    End Sub

    Private Sub ForwardButton_Click(sender As Object, e As RoutedEventArgs)
        If _session.ActiveTab.CanGoForward Then
            _session.ActiveTab.Forward()
            _engine.GoForward()
            RefreshTabsList()
        End If
    End Sub

    Private Sub ReloadButton_Click(sender As Object, e As RoutedEventArgs)
        HideError()
        _engine.Reload()
    End Sub

    Private Sub StopButton_Click(sender As Object, e As RoutedEventArgs)
        _navigationToken = Nothing
        LoadProgress.Value = 0
        StatusText.Text = String.Empty
        _engine.Stop()
    End Sub

    Private Sub TabsButton_Click(sender As Object, e As RoutedEventArgs)
        _session.NewTab()
        RefreshTabsList()
        _engine.Navigate(_appSettings.Homepage)
    End Sub

    Private Sub TabsList_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If _refreshingTabs Then
            Return
        End If
        Dim picked As Integer = TabsList.SelectedIndex
        If picked < 0 OrElse picked >= _session.Tabs.Count Then
            Return
        End If
        _session.ActivateTab(picked)
        Dim tabUrl As String = _session.ActiveTab.Url
        If String.IsNullOrEmpty(tabUrl) Then
            tabUrl = _appSettings.Homepage
        End If
        _engine.Navigate(tabUrl)
        RefreshTabsList()
    End Sub

    Private Sub CloseTabButton_Click(sender As Object, e As RoutedEventArgs)
        _session.CloseActiveTab()
        RefreshTabsList()
        Dim tabUrl As String = _session.ActiveTab.Url
        If String.IsNullOrEmpty(tabUrl) Then
            tabUrl = _appSettings.Homepage
        End If
        _engine.Navigate(tabUrl)
    End Sub

    Private Sub HistoryList_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim picked As Integer = HistoryList.SelectedIndex
        If picked < 0 Then
            Return
        End If
        Dim pickedUrl As String = TryCast(HistoryList.SelectedItem, String)
        If String.IsNullOrEmpty(pickedUrl) Then
            Return
        End If
        SettingsOverlay.Visibility = Visibility.Collapsed
        _session.ActiveTab.PushHistory(pickedUrl)
        _engine.Navigate(pickedUrl)
        HistoryList.SelectedIndex = -1
    End Sub

    Private Sub ClearHistoryButton_Click(sender As Object, e As RoutedEventArgs)
        _historyStore.Clear()
        SavePersistedState()
        RefreshHistoryList()
    End Sub

    Private Sub FavoritesList_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim pickedUrl As String = TryCast(FavoritesList.SelectedItem, String)
        If String.IsNullOrEmpty(pickedUrl) Then
            Return
        End If
        SettingsOverlay.Visibility = Visibility.Collapsed
        _session.ActiveTab.PushHistory(pickedUrl)
        _engine.Navigate(pickedUrl)
        FavoritesList.SelectedIndex = -1
    End Sub

    Private Sub AddFavoriteButton_Click(sender As Object, e As RoutedEventArgs)
        Dim tabUrl As String = _session.ActiveTab.Url
        If String.IsNullOrEmpty(tabUrl) Then
            Return
        End If
        _favoritesStore.Add(tabUrl, tabUrl)
        SavePersistedState()
        RefreshFavoritesList()
    End Sub

    Private Sub RemoveFavoriteButton_Click(sender As Object, e As RoutedEventArgs)
        Dim pickedUrl As String = TryCast(FavoritesList.SelectedItem, String)
        If String.IsNullOrEmpty(pickedUrl) Then
            pickedUrl = _session.ActiveTab.Url
        End If
        _favoritesStore.Remove(pickedUrl)
        SavePersistedState()
        RefreshFavoritesList()
    End Sub

    Private Sub SecurityDetailsButton_Click(sender As Object, e As RoutedEventArgs)
        Dim tabUrl As String = _session.ActiveTab.Url
        Dim isHttps As Boolean = tabUrl IsNot Nothing AndAlso tabUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        Dim caps = _engine.Capabilities
        Dim detailText As String
        If isHttps Then
            detailText = Localizer.Get("SecuritySecure") & " " & Localizer.Get("SecurityWebViewCeiling") & " UA=" & _session.EffectiveUserAgent
        Else
            detailText = Localizer.Get("SecurityInsecure")
        End If
        ErrorText.Text = detailText & vbCrLf & caps.ToString()
        ErrorText.Visibility = Visibility.Visible
    End Sub

    ''' <summary>Tracker check for top-level navigations (subresources excluded by the OS).</summary>
    Private Function IsBlockedTrackerUrl(pageUrl As String) As Boolean
        If Not _appSettings.BlockTrackers Then
            Return False
        End If
        If String.IsNullOrEmpty(pageUrl) Then
            Return False
        End If
        Dim parsedUri As Uri = Nothing
        If Not Uri.TryCreate(pageUrl, UriKind.Absolute, parsedUri) Then
            Return False
        End If
        Return TrackerBlocklist.ShouldBlock(parsedUri.Host)
    End Function

    Private Sub ShowBlockedTracker()
        ErrorText.Text = Localizer.Get("BlockedTracker")
        ErrorText.Visibility = Visibility.Visible
    End Sub

    ''' <summary>Snapshot open tabs for restore; skipped entirely in private mode.</summary>
    Private Sub SaveSessionTabs()
        Try
            Dim openUrls As New List(Of String)()
            For Each openTab In _session.Tabs
                If Not String.IsNullOrEmpty(openTab.Url) Then
                    openUrls.Add(openTab.Url)
                End If
            Next
            _appSettings.SetSessionTabs(openUrls)
        Catch ex As Exception
        End Try
    End Sub

    Private Sub FindButton_Click(sender As Object, e As RoutedEventArgs)
        If FindBar.Visibility = Visibility.Visible Then
            FindBar.Visibility = Visibility.Collapsed
        Else
            FindBar.Visibility = Visibility.Visible
            FindBox.Focus(FocusState.Programmatic)
        End If
    End Sub

    Private Sub FindBox_KeyDown(sender As Object, e As KeyRoutedEventArgs)
        If e.Key <> Windows.System.VirtualKey.Enter Then Return
        DoFindNext()
    End Sub

    Private Sub FindNextButton_Click(sender As Object, e As RoutedEventArgs)
        DoFindNext()
    End Sub

    Private Sub FindCloseButton_Click(sender As Object, e As RoutedEventArgs)
        FindBar.Visibility = Visibility.Collapsed
        FindResult.Text = String.Empty
    End Sub

    Private Async Sub DoFindNext()
        Try
            Dim searchTerm As String = FindBox.Text
            If String.IsNullOrEmpty(searchTerm) Then
                Return
            End If
            Dim foundIt As Boolean = Await DirectCast(_engine, TridentEngine).FindInPageAsync(searchTerm)
            If foundIt Then
                FindResult.Text = String.Empty
            Else
                FindResult.Text = Localizer.Get("FindNoMatch")
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Async Sub ReadingButton_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim entered As Boolean = Await DirectCast(_engine, TridentEngine).EnterReadingModeAsync()
            If Not entered Then
                ErrorText.Text = Localizer.Get("ErrorPageFailed")
                ErrorText.Visibility = Visibility.Visible
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub ShareButton_Click(sender As Object, e As RoutedEventArgs)
        Try
            DataTransferManager.ShowShareUI()
        Catch ex As Exception
        End Try
    End Sub

    Private Sub OnShareRequested(sender As DataTransferManager, e As DataRequestedEventArgs)
        Try
            Dim pageUrl As String = _session.ActiveTab.Url
            Dim sharedUri As Uri = Nothing
            If Not Uri.TryCreate(If(pageUrl, String.Empty), UriKind.Absolute, sharedUri) Then
                e.Request.FailWithDisplayText(Localizer.Get("ErrorPageFailed"))
                Return
            End If
            e.Request.Data.Properties.Title = If(String.IsNullOrEmpty(pageUrl), Localizer.Get("AppName"), pageUrl)
            ' SetWebLink, not SetUri: the OS deprecates DataPackage.SetUri with this
            ' exact advice, and a shared page URL is a web link by definition.
            e.Request.Data.SetWebLink(sharedUri)
        Catch ex As Exception
            e.Request.FailWithDisplayText(Localizer.Get("ErrorPageFailed"))
        End Try
    End Sub

    Private Sub OnHardwareBackPressed(sender As Object, e As BackPressedEventArgs)
        If _session.ActiveTab.CanGoBack Then
            _session.ActiveTab.Back()
            _engine.GoBack()
            RefreshTabsList()
            e.Handled = True
        End If
    End Sub

    Private Sub OnNavigationStarting(sender As WebView, e As WebViewNavigationStartingEventArgs)
        If e.Uri IsNot Nothing AndAlso _appSettings.LiteRedirects Then
            Dim liteUrl As String = LiteRedirects.RedirectUrl(e.Uri.ToString())
            If Not String.IsNullOrEmpty(liteUrl) AndAlso liteUrl <> e.Uri.ToString() Then
                e.Cancel = True
                _session.ActiveTab.ReplaceCurrent(liteUrl)
                AddressBox.Text = liteUrl
                _engine.Navigate(liteUrl)
                Return
            End If
        End If
        If e.Uri IsNot Nothing AndAlso IsBlockedTrackerUrl(e.Uri.ToString()) Then
            e.Cancel = True
            ShowBlockedTracker()
            Return
        End If
        _navigationToken = e.Uri
        LoadProgress.Value = 10
        StatusText.Text = Localizer.Get("Loading")
        HideError()
    End Sub

    Private Async Sub OnDOMContentLoaded(sender As WebView, e As WebViewDOMContentLoadedEventArgs)
        Try
            Await DirectCast(_engine, TridentEngine).InjectPolyfillAsync()
            If _appSettings.NightMode Then
                Await DirectCast(_engine, TridentEngine).SetNightModeAsync(True)
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Async Sub OnNavigationCompleted(sender As WebView, e As WebViewNavigationCompletedEventArgs)
        If _navigationToken Is Nothing Then Return
        _navigationToken = Nothing

        If Not e.IsSuccess Then
            ' A failed navigation must not be recorded as a visit or reported as
            ' 100% loaded. The OS status name is shown verbatim (it is a stable
            ' enum name, not a translated sentence) next to a localized reason.
            LoadProgress.Value = 0
            Dim failureReason As String
            Select Case e.WebErrorStatus
                Case WebErrorStatus.HostNameNotResolved, WebErrorStatus.CannotConnect, WebErrorStatus.ServerUnreachable, WebErrorStatus.Timeout, WebErrorStatus.ConnectionAborted
                    failureReason = Localizer.Get("ErrorNoConnection")
                Case Else
                    failureReason = Localizer.Get("ErrorNavigationFailed")
            End Select
            ErrorText.Text = failureReason & " (" & e.WebErrorStatus.ToString() & ")"
            ErrorText.Visibility = Visibility.Visible
            StatusText.Text = String.Empty
            Return
        End If

        LoadProgress.Value = 100
        StatusText.Text = Localizer.Get("LoadComplete")
        Dim pageUrl As String = e.Uri.ToString()
        _session.ActiveTab.ReplaceCurrent(pageUrl)
        AddressBox.Text = _session.ActiveTab.Url
        If Not _session.PrivateMode Then
            _historyStore.Add(_session.ActiveTab.Url, String.Empty)
            SaveSessionTabs()
        End If
        SavePersistedState()
        RefreshTabsList()
        RefreshHistoryList()
        UpdateSecurityGlyph()
        Try
            Await DirectCast(_engine, TridentEngine).InjectPolyfillAsync()
            If _appSettings.NightMode Then
                Await DirectCast(_engine, TridentEngine).SetNightModeAsync(True)
            End If
        Catch ex As Exception
        End Try
        Try
            Dim compatProbe As New CompatibilityProbe()
            Dim compatReport = Await compatProbe.RunAsync(_engine)
            If compatReport.MissingFeatures.Count >= 8 Then
                Dim entered As Boolean = Await DirectCast(_engine, TridentEngine).EnterReadingModeAsync()
                If entered Then
                    ErrorText.Text = Localizer.Get("ReaderFallback")
                    ErrorText.Visibility = Visibility.Visible
                End If
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub HideError()
        ErrorText.Visibility = Visibility.Collapsed
        ErrorText.Text = String.Empty
    End Sub

    Private Sub UpdateSecurityGlyph()
        Dim tabUrl As String = _session.ActiveTab.Url
        Dim isHttps As Boolean = tabUrl IsNot Nothing AndAlso tabUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)

        If isHttps Then
            SecurityGlyph.Text = If(_engine.Capabilities.SupportsTls13,
                                    Char.ConvertFromUtf32(&H1F512),
                                    Char.ConvertFromUtf32(&H1F513))
        Else
            SecurityGlyph.Text = ChrW(&H26A0)
        End If
    End Sub

    Private Sub SettingsButton_Click(sender As Object, e As RoutedEventArgs)
        RefreshTabsList()
        RefreshHistoryList()
        RefreshFavoritesList()
        SettingsOverlay.Visibility = Visibility.Visible
    End Sub

    Private Sub DiagnosticsButton_Click(sender As Object, e As RoutedEventArgs)
        SettingsOverlay.Visibility = Visibility.Collapsed
        DiagnosticsOverlay.Visibility = Visibility.Visible
    End Sub

    Private Sub DiagnosticsBackButton_Click(sender As Object, e As RoutedEventArgs)
        DiagnosticsOverlay.Visibility = Visibility.Collapsed
        SettingsOverlay.Visibility = Visibility.Visible
    End Sub

    Private Sub CloseSettingsButton_Click(sender As Object, e As RoutedEventArgs)
        SavePersistedState()
        ApplyLocalizedStrings()
        SettingsOverlay.Visibility = Visibility.Collapsed
    End Sub

    Private Async Sub CompatProbeButton_Click(sender As Object, e As RoutedEventArgs)
        CompatProbeButton.IsEnabled = False
        Try
            Dim probe As New CompatibilityProbe()
            Dim report = Await probe.RunAsync(_engine)

            If Not report.CouldRun Then
                ' Nothing was measured, so say that. Reporting "no missing web
                ' features" here was a claim the probe had not earned, and it is
                ' the shape of lie this project exists to avoid.
                CompatProbeResult.Text = Localizer.Get("ProbeNotRun")
            ElseIf report.IsFullyCompatible Then
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

    Private Async Sub TlsProbeButton_Click(sender As Object, e As RoutedEventArgs)
        TlsProbeButton.IsEnabled = False
        Try
            Dim probeHost As String = TlsHostBox.Text.Trim()
            If String.IsNullOrEmpty(probeHost) Then
                Dim tabUrl As String = _session.ActiveTab.Url
                If Not String.IsNullOrEmpty(tabUrl) Then
                    Dim parsedUri As Uri = Nothing
                    If Uri.TryCreate(tabUrl, UriKind.Absolute, parsedUri) Then
                        probeHost = parsedUri.Host
                    End If
                End If
            End If
            If String.IsNullOrEmpty(probeHost) Then
                probeHost = "example.com"
            End If
            Dim runnerResult = Await BrowserForWP.Diagnostics.TlsProbeRunner.RunAsync(probeHost, _appSettings.DohUrl, _pinTable)
            ' SecurityTls13 exists for exactly this state, and only for this state:
            ' the app's own transport negotiated TLS 1.3. It must never be shown
            ' for WebView traffic, which rides Schannel and tops out at TLS 1.2.
            If runnerResult.IsTls13 Then
                TlsProbeResult.Text = Localizer.Get("SecurityTls13") & vbCrLf & runnerResult.ToString()
            Else
                TlsProbeResult.Text = runnerResult.ToString()
            End If
            ' Surface the localized mismatch sentence instead of leaving the user
            ' to spot the English "pin-MISMATCH" token inside the detail line.
            If runnerResult.PinMismatch Then
                PinStatus.Text = Localizer.Get("PinMismatch")
            End If
        Catch ex As Exception
            TlsProbeResult.Text = Localizer.Get("ErrorTlsHandshake") & " " & ex.Message
        Finally
            TlsProbeButton.IsEnabled = True
        End Try
    End Sub

    Private Sub PinAddButton_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim hostKey As String = PinHostBox.Text.Trim()
            Dim pinText As String = PinValueBox.Text.Trim()
            If String.IsNullOrEmpty(hostKey) OrElse String.IsNullOrEmpty(pinText) Then
                Return
            End If
            _pinTable.Add(hostKey, pinText)
            SavePersistedState()
            PinStatus.Text = Localizer.Get("PinStored")
        Catch ex As Exception
            PinStatus.Text = Localizer.Get("ErrorPageFailed")
        End Try
    End Sub

    Private Sub PinRemoveButton_Click(sender As Object, e As RoutedEventArgs)
        Dim hostKey As String = PinHostBox.Text.Trim()
        If String.IsNullOrEmpty(hostKey) Then
            Return
        End If
        _pinTable.Remove(hostKey)
        SavePersistedState()
        PinStatus.Text = Localizer.Get("PinStored")
    End Sub

    ''' <summary>
    ''' Fetch the active tab's URL over the app's own TLS 1.3 transport and show
    ''' what the native pipeline understood. This is the demonstration that the
    ''' engine exists: it is the only place in the product where a page LOAD goes
    ''' over Tls13Client rather than through the WebView's Schannel path.
    ''' </summary>
    Private Async Sub ParsePageButton_Click(sender As Object, e As RoutedEventArgs)
        ParsePageButton.IsEnabled = False
        Try
            Dim tabUrl As String = _session.ActiveTab.Url
            If String.IsNullOrEmpty(tabUrl) Then
                ParseResult.Text = Localizer.Get("ParseNoDocument")
                Return
            End If

            ' The pin table goes in: a page load now travels the same TLS 1.3 path the
            ' probe uses, so a stored pin has to be enforced here too, not only there.
            Dim fetcher As New BrowserForWP.Diagnostics.NetDocumentFetcher(_pinTable)
            Dim response As DocumentResponse = Await fetcher.FetchAsync(tabUrl, _appSettings.DohUrl)

            If Not String.IsNullOrEmpty(response.ErrorMessage) Then
                ParseResult.Text = Localizer.Get("ParseFailed") & " " & response.ErrorMessage
                Return
            End If
            If Not response.IsHtml Then
                ParseResult.Text = Localizer.Get("ParseNoDocument")
                Return
            End If

            Dim boxTree As BoxNode = BoxTreeBuilder.BuildPage(response.Text, InlineStyleText(response.Text))
            ' Every user-visible word comes from the resw, including the unit: the
            ' plan wrote " box(es)" inline, which is exactly the hardcoded English
            ' this repository forbids.
            Dim headerText As String = response.FinalUrl & "  [" & response.EffectiveCharset & "]  " &
                                       Localizer.Get("ParseBoxCount") & boxTree.DescendantCount().ToString() & vbCrLf
            ParseResult.Text = headerText & DocumentDumper.Dump(boxTree)
        Catch ex As Exception
            ParseResult.Text = Localizer.Get("ParseFailed") & " " & ex.Message
        Finally
            ParsePageButton.IsEnabled = True
        End Try
    End Sub

    ''' <summary>
    ''' Fetch the current tab's page over the app's own TLS 1.3 transport and draw it
    ''' with the native engine. This is the first place where a page is RENDERED from
    ''' the code in BrowserForWP.Core: the parse button above stops at a box tree, and
    ''' the WebView never involves Core at all.
    ''' Types are fully qualified because this file's Imports cover the Core engine and
    ''' this app's own diagnostics, not Rendering.
    ''' </summary>
    Private Async Sub RenderNativeButton_Click(sender As Object, e As RoutedEventArgs)
        RenderNativeButton.IsEnabled = False
        Try
            Dim tabUrl As String = _session.ActiveTab.Url
            If String.IsNullOrEmpty(tabUrl) Then
                ParseResult.Text = Localizer.Get("ParseNoDocument")
                Return
            End If

            Dim fetcher As New BrowserForWP.Diagnostics.NetDocumentFetcher(_pinTable)
            Dim response As DocumentResponse = Await fetcher.FetchAsync(tabUrl, _appSettings.DohUrl)

            If Not String.IsNullOrEmpty(response.ErrorMessage) Then
                ParseResult.Text = Localizer.Get("ParseFailed") & " " & response.ErrorMessage
                Return
            End If
            If Not response.IsHtml Then
                ParseResult.Text = Localizer.Get("ParseNoDocument")
                Return
            End If

            ' The preview is laid out for the width the host actually has. Before the
            ' first layout pass that is 0, and the fallback keeps the button usable.
            Dim viewportPx As Double = NativePreviewHost.ActualWidth
            If viewportPx < 1 Then viewportPx = 360

            Dim boxTree As BoxNode = BoxTreeBuilder.BuildPage(response.Text, InlineStyleText(response.Text))
            Dim measurer As New BrowserForWP.Rendering.XamlTextMeasurer()
            Dim laidOut As BrowserForWP.Core.Engine.Native.LayoutBox =
                BrowserForWP.Core.Engine.Native.BlockLayout.Layout(boxTree, viewportPx, measurer)
            NativePreviewHost.Child = BrowserForWP.Rendering.XamlBoxRenderer.Render(laidOut)

            ParseResult.Text = CInt(laidOut.WidthPx).ToString() & " x " & CInt(laidOut.HeightPx).ToString() &
                               "  " & laidOut.DescendantCount().ToString()
        Catch ex As Exception
            ParseResult.Text = Localizer.Get("ParseFailed") & " " & ex.Message
        Finally
            RenderNativeButton.IsEnabled = True
        End Try
    End Sub

    ''' <summary>
    ''' Ask the hosted engine how it is configured. This is the instrument behind
    ''' docs/MAINTAINING.md's "IE-adaptation is closed": the four levers a
    ''' "re-configure Trident" plan needs are absent from the API surface, and this
    ''' is how a handset confirms that instead of taking our word for it. Record the
    ''' mode it reports in that section when someone runs it on a device.
    ''' </summary>
    Private Async Sub IeModeButton_Click(sender As Object, e As RoutedEventArgs)
        IeModeButton.IsEnabled = False
        Try
            Dim modeReport As IeModeReport = Await IeModeProbe.RunAsync(_engine)
            If modeReport Is Nothing Then
                IeModeResult.Text = Localizer.Get("ProbeNotRun")
                Return
            End If
            IeModeResult.Text = Localizer.Get("IeModeReport") & modeReport.DocumentMode.ToString() & vbCrLf &
                                modeReport.RawJson
        Catch ex As Exception
            IeModeResult.Text = Localizer.Get("ErrorPageFailed")
        Finally
            IeModeButton.IsEnabled = True
        End Try
    End Sub

    ''' <summary>
    ''' Collect the text of every inline style element, because the tree builder
    ''' drops script and style bodies. Anything more would need a real head parser.
    ''' </summary>
    Private Shared Function InlineStyleText(html As String) As String
        If String.IsNullOrEmpty(html) Then Return String.Empty
        Dim collected As New System.Text.StringBuilder()
        Dim lowered As String = html.ToLowerInvariant()
        Dim searchFrom As Integer = 0
        While True
            Dim openAt As Integer = lowered.IndexOf("<style", searchFrom, StringComparison.Ordinal)
            If openAt < 0 Then Exit While
            Dim bodyStart As Integer = lowered.IndexOf(">"c, openAt)
            If bodyStart < 0 Then Exit While
            Dim closeAt As Integer = lowered.IndexOf("</style", bodyStart)
            If closeAt < 0 Then Exit While
            collected.Append(html.Substring(bodyStart + 1, closeAt - bodyStart - 1))
            collected.Append(vbLf)
            searchFrom = closeAt + 1
        End While
        Return collected.ToString()
    End Function
End Class
