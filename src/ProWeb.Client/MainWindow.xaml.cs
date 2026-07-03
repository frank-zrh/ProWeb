using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CefSharp;
using CefSharp.Wpf;
using ProWeb.Client.Core;

namespace ProWeb.Client;

/// <summary>
/// Main browser window. Wires the address bar, navigation/reload-stop buttons, tab strip, security
/// badge, keyboard shortcuts, and settings dialog (all backed by ProWeb.Client.Core models) to the
/// CefSharp browser, and installs the secure request handler that routes traffic through the
/// encrypted proxy channel.
/// </summary>
public partial class MainWindow : Window
{
    private readonly TabCollection _tabs = new();
    private readonly ClosedTabStack _closedTabs = new();
    private readonly SessionContextAccessor _sessionAccessor = new();
    private readonly LoadProgressModel _progress = new();
    private readonly ClientSettingsStore _settingsStore = new(ClientSettingsStore.DefaultPath());

    private ClientSettings _settings;
    private ClientProxyChannel _channel = null!;
    private ProxyInterceptionCoordinator _coordinator = null!;
    private HttpClient _http = null!;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsStore.Load();
        InitializeChannel();

        Browser.RequestHandler = new SecureRequestHandler(_coordinator);
        Browser.LifeSpanHandler = new InAppPopupLifeSpanHandler(OpenInNewTab);
        Browser.FindHandler = new BrowserFindHandler(OnFindResult);
        Browser.AddressChanged += Browser_AddressChanged;
        Browser.TitleChanged += Browser_TitleChanged;
        Browser.LoadingStateChanged += Browser_LoadingStateChanged;
        Browser.LoadError += Browser_LoadError;

        RegisterShortcuts();

        var first = _tabs.AddTab();
        first.Title = "新标签页";
        RefreshTabStrip();
        UpdateSecurityBadge(SecureChannelState.Disconnected);

        Loaded += async (_, _) =>
        {
            NavigateHomeOrNewTab();
            await ConnectAsync();
        };
    }

    /// <summary>Navigates to the configured home page, or shows the new-tab empty state (UT-X-R3-002).</summary>
    private void NavigateHomeOrNewTab()
    {
        var home = HomeNavigationResolver.ResolveHomeTarget(_settings);
        if (home is null)
            ShowNewTabPage();
        else
            Navigate(home);
    }

    private void InitializeChannel()
    {
        _http = ClientChannelFactory.CreateHttpClient(_settings);
        _channel = new ClientProxyChannel(_http, ClientChannelFactory.ResolveDeviceId(_settings));
        _coordinator = new ProxyInterceptionCoordinator(_channel);
    }

    private async System.Threading.Tasks.Task ConnectAsync()
    {
        UpdateSecurityBadge(SecureChannelState.Connecting);
        StatusText.Text = "正在连接安全代理服务…";
        try
        {
            await _channel.HandshakeAsync();
            _sessionAccessor.ActiveSessionId = _channel.SessionId ?? string.Empty;
            UpdateSecurityBadge(SecureChannelState.Secure);
            StatusText.Text = "已连接";
        }
        catch (Exception ex)
        {
            UpdateSecurityBadge(SecureChannelState.Disconnected);
            StatusText.Text = $"连接失败：{ex.Message}";
        }
    }

    private void UpdateSecurityBadge(SecureChannelState state)
    {
        var model = SecurityStatusModel.For(state);
        SecurityBadge.Text = model.Text;
        SecurityBadge.ToolTip = model.Tooltip;
        SecurityBadge.Foreground = new SolidColorBrush(
            model.IsSecure ? Color.FromRgb(0x2e, 0x7d, 0x32) : Color.FromRgb(0xb7, 0x1c, 0x1c));
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void Navigate(string input)
    {
        var validation = AddressValidationModel.Evaluate(input);
        if (validation.IsError)
        {
            ShowAddressError(validation.Message);
            return;
        }

        ClearAddressError();
        var url = UrlNormalizer.Normalize(input);
        _tabs.Active?.Navigation.Navigate(url);
        AddressBar.Text = url;
        Browser.Load(url);
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Navigate(AddressBar.Text);
    }

    private void AddressBar_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var validation = AddressValidationModel.Evaluate(AddressBar.Text);
        if (validation.IsError)
            ShowAddressError(validation.Message);
        else
            ClearAddressError();
    }

    private void ShowAddressError(string message)
    {
        AddressBar.BorderBrush = new SolidColorBrush(Color.FromRgb(0xc0, 0x39, 0x2b));
        AddressErrorText.Text = message;
        AddressErrorText.Visibility = Visibility.Visible;
        StatusText.Text = message;
    }

    private void ClearAddressError()
    {
        AddressBar.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        AddressErrorText.Visibility = Visibility.Collapsed;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => GoBack();

    private void ForwardButton_Click(object sender, RoutedEventArgs e) => GoForward();

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        // The primary button toggles between reload and stop based on load state.
        if (_progress.PrimaryAction == PrimaryLoadAction.Stop)
            Browser.GetBrowser()?.StopLoad();
        else
            Browser.Reload();
    }

    private void GoBack()
    {
        var url = _tabs.Active?.Navigation.GoBack();
        if (url is not null)
            Browser.Load(url);
    }

    private void GoForward()
    {
        var url = _tabs.Active?.Navigation.GoForward();
        if (url is not null)
            Browser.Load(url);
    }

    // ── Tabs ──────────────────────────────────────────────────────────────────

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => OpenNewTab();

    private void OpenNewTab()
    {
        var tab = _tabs.AddTab();
        tab.Title = "新标签页";
        RefreshTabStrip();
        NavigateHomeOrNewTab();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => GoHome();

    /// <summary>Navigates the active tab to the configured home page (UT-X-R3-002).</summary>
    private void GoHome()
    {
        var home = HomeNavigationResolver.ResolveHomeTarget(_settings);
        if (home is null)
        {
            ShowNewTabPage();
            StatusText.Text = "未设置主页，可在『设置 / 连接』中配置。";
        }
        else
        {
            Navigate(home);
        }
    }

    /// <summary>
    /// Opens <paramref name="url"/> in a new in-app tab through the encrypted proxy. Used for
    /// <c>target="_blank"</c>/<c>window.open</c> popups so they never direct-connect or get
    /// silently swallowed by Chromium (UT-X-R3-902).
    /// </summary>
    private void OpenInNewTab(string url)
    {
        Dispatcher.Invoke(() =>
        {
            if (!RequestSchemeClassifier.IsProxyable(url))
                return;
            var tab = _tabs.AddTab();
            tab.Title = "新标签页";
            RefreshTabStrip();
            Navigate(url);
        });
    }

    /// <summary>Reveals the in-page find bar and focuses it (UT-X-R3-003, Ctrl+F).</summary>
    private void OpenFindBar()
    {
        FindBar.Visibility = Visibility.Visible;
        FindInput.Focus();
        FindInput.SelectAll();
        if (!string.IsNullOrEmpty(FindInput.Text))
            DoFind(forward: true, findNext: false);
    }

    private void CloseFindBar()
    {
        try
        {
            Browser.GetBrowser()?.GetHost()?.StopFinding(clearSelection: true);
        }
        catch (Exception)
        {
            // Browser may not be initialised yet; nothing to stop.
        }

        FindBar.Visibility = Visibility.Collapsed;
        FindCount.Text = string.Empty;
    }

    private void DoFind(bool forward, bool findNext)
    {
        var text = FindInput.Text;
        IBrowserHost? host;
        try
        {
            host = Browser.GetBrowser()?.GetHost();
        }
        catch (Exception)
        {
            return;
        }

        if (host is null)
            return;

        if (string.IsNullOrEmpty(text))
        {
            host.StopFinding(clearSelection: true);
            FindCount.Text = string.Empty;
            return;
        }

        host.Find(text, forward, matchCase: false, findNext);
    }

    private void OnFindResult(int count, int activeOrdinal)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(FindInput.Text))
                FindCount.Text = string.Empty;
            else if (count <= 0)
                FindCount.Text = "无结果";
            else
                FindCount.Text = $"{activeOrdinal}/{count}";
        });
    }

    private void FindInput_TextChanged(object sender, TextChangedEventArgs e) => DoFind(forward: true, findNext: false);

    private void FindInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DoFind(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0, findNext: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFindBar();
            Browser.Focus();
            e.Handled = true;
        }
    }

    private void FindPrev_Click(object sender, RoutedEventArgs e) => DoFind(forward: false, findNext: true);

    private void FindNext_Click(object sender, RoutedEventArgs e) => DoFind(forward: true, findNext: true);

    private void FindClose_Click(object sender, RoutedEventArgs e)
    {
        CloseFindBar();
        Browser.Focus();
    }

    private void CloseActiveTab()
    {
        var active = _tabs.Active;
        if (active is null)
            return;
        if (active.CurrentUrl is { } url)
            _closedTabs.Push(new ClosedTab(url, active.Title));

        if (_tabs.CloseTab(active.Id))
        {
            RefreshTabStrip();
            LoadActiveTab();
        }
    }

    private void ReopenClosedTab()
    {
        var reopened = _closedTabs.Reopen();
        if (reopened is null)
        {
            StatusText.Text = "没有可重新打开的标签页。";
            return;
        }

        var tab = _tabs.AddTab();
        tab.Title = reopened.Title;
        RefreshTabStrip();
        Navigate(reopened.Url);
    }

    private void LoadActiveTab()
    {
        var url = _tabs.Active?.CurrentUrl;
        if (url is null)
            ShowNewTabPage();
        else
            Browser.Load(url);
    }

    private void RefreshTabStrip()
    {
        TabStripPanel.Children.Clear();
        foreach (var tab in _tabs.Tabs)
        {
            var isActive = ReferenceEquals(tab, _tabs.Active);
            var button = new System.Windows.Controls.Button
            {
                Content = string.IsNullOrEmpty(tab.Title) ? "新标签页" : tab.Title,
                Tag = tab.Id,
                Margin = new Thickness(1, 2, 1, 0),
                Padding = new Thickness(8, 0, 8, 0),
                MaxWidth = 180,
                Background = isActive ? Brushes.White : Brushes.Transparent,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"标签页：{tab.Title}");
            button.Click += (_, _) =>
            {
                if (button.Tag is string id && _tabs.Activate(id))
                {
                    RefreshTabStrip();
                    LoadActiveTab();
                }
            };
            TabStripPanel.Children.Add(button);
        }
    }

    private void ShowNewTabPage()
    {
        AddressBar.Text = string.Empty;
        Browser.LoadHtml(NewTabPageModel.Render());
    }

    // ── Menu ──────────────────────────────────────────────────────────────────

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (MenuButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MenuButton;
            menu.IsOpen = true;
        }
    }

    private void Menu_NewTab(object sender, RoutedEventArgs e) => OpenNewTab();

    private void Menu_ReopenTab(object sender, RoutedEventArgs e) => ReopenClosedTab();

    private void Menu_CloseTab(object sender, RoutedEventArgs e) => CloseActiveTab();

    private void Menu_Settings(object sender, RoutedEventArgs e) => OpenSettings();

    private void Menu_Home(object sender, RoutedEventArgs e) => GoHome();

    private void Menu_Find(object sender, RoutedEventArgs e) => OpenFindBar();

    private void Menu_Shortcuts(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this, ShortcutCheatSheet.RenderText(), "键盘快捷键速查",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Menu_About(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            $"{AboutInfo.ProductName}\n{AboutInfo.Tagline}\n\n版本：{AboutInfo.Version}\n构建日期：{AboutInfo.BuildDate}",
            "关于 ProWeb", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Menu_Feedback(object sender, RoutedEventArgs e)
    {
        var requestId = _sessionAccessor.ActiveSessionId is { Length: > 0 } sid ? sid : null;
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(FeedbackInfo.BuildMailto(requestId)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"无法打开反馈入口：{ex.Message}";
        }
    }

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings.Clone()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = dialog.Result;
            _settingsStore.Save(_settings);
            _ = ReconnectAsync();
        }
    }

    /// <summary>
    /// Rebuilds the encrypted channel against the (possibly new) server endpoint and re-handshakes
    /// so a saved endpoint takes effect immediately, updating the security badge from the real
    /// result rather than telling the user to restart (UT-X-R3-004).
    /// </summary>
    private async System.Threading.Tasks.Task ReconnectAsync()
    {
        try
        {
            await _channel.CloseAsync();
        }
        catch (Exception)
        {
            // Best-effort close of the previous session.
        }

        _http?.Dispose();
        InitializeChannel();
        Browser.RequestHandler = new SecureRequestHandler(_coordinator);
        _sessionAccessor.ActiveSessionId = string.Empty;
        await ConnectAsync();
    }

    // ── Keyboard shortcuts (single source of truth: Core ShortcutTable) ─────────

    private void RegisterShortcuts()
    {
        var actions = new Dictionary<BrowserCommand, Action>
        {
            [BrowserCommand.NewTab] = OpenNewTab,
            [BrowserCommand.CloseTab] = CloseActiveTab,
            [BrowserCommand.ReopenClosedTab] = ReopenClosedTab,
            [BrowserCommand.FocusAddressBar] = () => { AddressBar.Focus(); AddressBar.SelectAll(); },
            [BrowserCommand.Reload] = () => Browser.Reload(),
            [BrowserCommand.StopLoading] = () => Browser.GetBrowser()?.StopLoad(),
            [BrowserCommand.Back] = GoBack,
            [BrowserCommand.Forward] = GoForward,
            [BrowserCommand.OpenSettings] = OpenSettings,
            [BrowserCommand.FindInPage] = OpenFindBar,
            [BrowserCommand.GoHome] = GoHome,
        };

        var converter = new KeyGestureConverter();
        foreach (var binding in ShortcutTable.Bindings)
        {
            if (!actions.TryGetValue(binding.Command, out var action))
                continue;
            try
            {
                if (converter.ConvertFromString(binding.Gesture) is KeyGesture gesture)
                    InputBindings.Add(new KeyBinding(new RelayCommand(action), gesture));
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                // Skip gestures WPF cannot represent without a modifier (e.g. bare Esc); the menu
                // and buttons still expose the action.
            }
        }
    }

    // ── Browser events ──────────────────────────────────────────────────────────

    private void Browser_AddressChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        var url = e.NewValue?.ToString() ?? string.Empty;
        Dispatcher.Invoke(() =>
        {
            // Only real (proxyable) navigations belong in the address bar and history. Internal
            // pages (about:blank from the new-tab LoadHtml) clear the bar and are not recorded.
            if (RequestSchemeClassifier.IsProxyable(url))
            {
                AddressBar.Text = url;
                // Record link-initiated navigations (Chromium drives these — Navigate() already
                // records address-bar/back/forward loads, which no-op here on the same URL) so the
                // NavigationState back/forward stack stays in sync with what the user sees.
                _tabs.Active?.Navigation.Navigate(url);
            }
            else
            {
                AddressBar.Text = string.Empty;
            }

            UpdateNavButtons();
        });
    }

    /// <summary>Reflects the tab's NavigationState (the single source of truth for back/forward).</summary>
    private void UpdateNavButtons()
    {
        var nav = _tabs.Active?.Navigation;
        BackButton.IsEnabled = nav?.CanGoBack ?? false;
        ForwardButton.IsEnabled = nav?.CanGoForward ?? false;
    }

    private void Browser_TitleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var title = e.NewValue?.ToString() ?? "ProWeb";
            if (_tabs.Active is not null)
                _tabs.Active.Title = title;
            Title = $"ProWeb — {title}";
            RefreshTabStrip();
        });
    }

    private void Browser_LoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.IsLoading)
                _progress.Start();
            else
                _progress.Complete();

            StatusText.Text = _progress.IsLoading ? "加载中…" : "就绪";
            ReloadButton.Content = _progress.ActionGlyph;
            System.Windows.Automation.AutomationProperties.SetName(ReloadButton, _progress.ActionLabel);
            ReloadButton.ToolTip = _progress.IsLoading ? "停止加载 (Esc)" : "重新加载 (F5)";
            LoadProgress.Visibility = _progress.ProgressVisible ? Visibility.Visible : Visibility.Collapsed;
            UpdateNavButtons();
        });
    }

    private void Browser_LoadError(object? sender, LoadErrorEventArgs e)
    {
        // Ignore sub-frame errors and user-initiated aborts.
        if (!e.Frame.IsMain || e.ErrorCode == CefErrorCode.Aborted)
            return;

        var requestId = _sessionAccessor.ActiveSessionId is { Length: > 0 } sid ? sid : "n/a";
        var html = ErrorPageModel.RenderLoadFailure(e.FailedUrl, e.ErrorCode.ToString(), e.ErrorText, requestId);
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = $"加载失败：{e.ErrorText}";
            Browser.LoadHtml(html);
        });
    }
}

/// <summary>Minimal ICommand adapter so Core-defined shortcuts can drive window actions.</summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action _action;

    public RelayCommand(Action action) => _action = action;

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _action();
}

/// <summary>
/// Intercepts popups (<c>target="_blank"</c>, <c>window.open</c>) and re-opens the target URL in an
/// in-app tab via the encrypted proxy instead of letting Chromium open an out-of-process popup that
/// would bypass interception. Returning <c>true</c> from <see cref="OnBeforePopup"/> cancels the
/// native popup (UT-X-R3-902).
/// </summary>
internal sealed class InAppPopupLifeSpanHandler : ILifeSpanHandler
{
    private readonly Action<string> _openInApp;

    public InAppPopupLifeSpanHandler(Action<string> openInApp) => _openInApp = openInApp;

    public bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl,
        string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture,
        IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings,
        ref bool noJavascriptAccess, out IWebBrowser? newBrowser)
    {
        newBrowser = null;
        if (!string.IsNullOrWhiteSpace(targetUrl))
            _openInApp(targetUrl);
        return true; // Cancel the native popup; we handle it in-app.
    }

    public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }

    public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser) => false;

    public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }
}

/// <summary>
/// Bridges CefSharp find results back to the UI so the find bar can show the active/total match
/// count (UT-X-R3-003).
/// </summary>
internal sealed class BrowserFindHandler : IFindHandler
{
    private readonly Action<int, int> _onResult;

    public BrowserFindHandler(Action<int, int> onResult) => _onResult = onResult;

    public void OnFindResult(
        IWebBrowser chromiumWebBrowser, IBrowser browser, int identifier, int count,
        CefSharp.Structs.Rect selectionRect, int activeMatchOrdinal, bool finalUpdate)
    {
        _onResult(count, activeMatchOrdinal);
    }
}
