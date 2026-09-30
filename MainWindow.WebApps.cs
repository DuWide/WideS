using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DevCockpit;

public partial class MainWindow
{
    private MessengerWebAppsView? _messengerWebApps;
    private WebAppView? _musicWebApp;
    private WebAppView? _tikTokWebApp;
    private WebAppView? _mangaWebApp;
    private DispatcherTimer? _mangaUrlSaveTimer;
    private string? _pendingMangaUrl;

    // Режим чтения: без заголовка и боковой панели, окно на весь монитор (поверх панели задач).
    private bool _immersiveMode;
    private bool _immersiveByPage;
    private WindowState _stateBeforeImmersive = WindowState.Normal;
    private Thickness _rootBorderBeforeImmersive;

    private IWebNavigationHost? CurrentWebNavigation => _currentViewKey switch
    {
        "messengers" => _messengerWebApps,
        "music" => _musicWebApp,
        "tiktok" => _tikTokWebApp,
        "manga" => _mangaWebApp,
        _ => null
    };

    private void ShowMessengers()
    {
        EnterView("messengers");
        _viewScope = "messengers";
        SetTitle("Мессенджеры", "WhatsApp и Telegram внутри WideS");
        UseWebContentLayout();

        if (_messengerWebApps is null)
        {
            _messengerWebApps = new MessengerWebAppsView();
            _messengerWebApps.NavigationStateChanged += WebNavigationStateChanged;
            _messengerWebApps.ZoomPercentChanged += WebZoomChanged;
            _messengerWebApps.ZoomPercent = _settings.WebAppZoomPercent;
        }

        ContentHost.Content = _messengerWebApps;
        UpdateNavigationButtons();
    }

    private void ShowMusic()
    {
        EnterView("music");
        _viewScope = "music";
        SetTitle("Музыка", "YouTube Music внутри WideS");
        UseWebContentLayout();

        if (_musicWebApp is null)
        {
            _musicWebApp = new WebAppView(WebAppDefinitions.YouTubeMusic, showToolbar: false);
            _musicWebApp.NavigationStateChanged += WebNavigationStateChanged;
            _musicWebApp.ZoomPercentChanged += WebZoomChanged;
            _musicWebApp.ZoomPercent = _settings.WebAppZoomPercent;
        }

        ContentHost.Content = _musicWebApp;
        UpdateNavigationButtons();
    }

    private void ShowTikTok()
    {
        EnterView("tiktok");
        _viewScope = "tiktok";
        SetTitle("TikTok", "TikTok внутри WideS");
        UseWebContentLayout();

        if (_tikTokWebApp is null)
        {
            _tikTokWebApp = new WebAppView(WebAppDefinitions.TikTok, showToolbar: false);
            _tikTokWebApp.NavigationStateChanged += WebNavigationStateChanged;
            _tikTokWebApp.ZoomPercentChanged += WebZoomChanged;
            _tikTokWebApp.ZoomPercent = _settings.WebAppZoomPercent;
        }

        ContentHost.Content = _tikTokWebApp;
        UpdateNavigationButtons();
    }

    private void ShowManga()
    {
        EnterView("manga");
        _viewScope = "manga";
        SetTitle("Манга", "MangaLib внутри WideS · F11 — режим чтения");
        UseWebContentLayout();

        if (_mangaWebApp is null)
        {
            _mangaWebApp = new WebAppView(WebAppDefinitions.MangaLib, showToolbar: true, enableReaderMode: true);
            // Продолжаем с той страницы (главы), на которой остановились в прошлый раз.
            if (Uri.TryCreate(_settings.MangaLibLastUrl, UriKind.Absolute, out var last) && IsRememberableMangaUri(last))
            {
                _mangaWebApp.StartUri = last;
            }

            _mangaWebApp.NavigationStateChanged += WebNavigationStateChanged;
            _mangaWebApp.ZoomPercentChanged += MangaZoomChanged;
            _mangaWebApp.PageUrlChanged += MangaPageUrlChanged;
            _mangaWebApp.ReaderModeToggleRequested += MangaReaderModeToggleRequested;
            _mangaWebApp.FullScreenElementChanged += MangaFullScreenElementChanged;
            _mangaWebApp.ZoomPercent = _settings.MangaLibZoomPercent;
            _mangaWebApp.SetReaderMode(_immersiveMode);
            // Обычно инструменты в заголовке окна; своя панель нужна только в режиме чтения (заголовок скрыт).
            _mangaWebApp.SetToolbarVisible(_immersiveMode);
        }

        ContentHost.Content = _mangaWebApp;
        UpdateNavigationButtons();
    }

    /// <summary>Масштаб читалки хранится отдельно: мангу часто удобнее читать крупнее, чем мессенджеры.</summary>
    private void MangaZoomChanged(object? sender, EventArgs e)
    {
        UpdateWebTools();
        if (_mangaWebApp is null || _mangaWebApp.ZoomPercent == _settings.MangaLibZoomPercent)
        {
            return;
        }

        _settings.MangaLibZoomPercent = _mangaWebApp.ZoomPercent;
        _settingsStore.Save(_settings);
    }

    private static bool IsRememberableMangaUri(Uri uri)
    {
        var host = uri.Host;
        var isMangaLib = host.Equals("mangalib.me", StringComparison.OrdinalIgnoreCase) ||
                         host.EndsWith(".mangalib.me", StringComparison.OrdinalIgnoreCase) ||
                         host.Equals("mangalib.org", StringComparison.OrdinalIgnoreCase) ||
                         host.EndsWith(".mangalib.org", StringComparison.OrdinalIgnoreCase);
        return uri.Scheme == Uri.UriSchemeHttps &&
               isMangaLib &&
               !uri.AbsolutePath.Contains("/auth", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>При листании глав адрес меняется часто — сохраняем не чаще раза в 3 секунды.</summary>
    private void MangaPageUrlChanged(object? sender, Uri uri)
    {
        if (!IsRememberableMangaUri(uri))
        {
            return;
        }

        _pendingMangaUrl = uri.AbsoluteUri;
        if (_mangaUrlSaveTimer is null)
        {
            _mangaUrlSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _mangaUrlSaveTimer.Tick += (_, _) => FlushMangaLastUrl();
        }

        if (!_mangaUrlSaveTimer.IsEnabled)
        {
            _mangaUrlSaveTimer.Start();
        }
    }

    private void FlushMangaLastUrl()
    {
        _mangaUrlSaveTimer?.Stop();
        if (_pendingMangaUrl is null || _pendingMangaUrl == _settings.MangaLibLastUrl)
        {
            _pendingMangaUrl = null;
            return;
        }

        _settings.MangaLibLastUrl = _pendingMangaUrl;
        _pendingMangaUrl = null;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch
        {
            // Не критично: в худшем случае откроется предыдущая сохранённая страница.
        }
    }

    private void MangaReaderModeToggleRequested(object? sender, EventArgs e)
    {
        SetImmersiveMode(!_immersiveMode);
    }

    /// <summary>Читалка сама попросила полноэкранный режим (Fullscreen API) — прячем и панель браузера.</summary>
    private void MangaFullScreenElementChanged(object? sender, bool fullScreen)
    {
        if (fullScreen)
        {
            if (!_immersiveMode)
            {
                _immersiveByPage = true;
                SetImmersiveMode(true);
            }

            _mangaWebApp?.SetToolbarVisible(false);
        }
        else
        {
            if (_immersiveByPage)
            {
                SetImmersiveMode(false);
            }
            else
            {
                _mangaWebApp?.SetToolbarVisible(_immersiveMode);
            }
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ctrl+1…9 — разделы бокового меню (подсказки у пунктов показывают номер).
        if (Keyboard.Modifiers == ModifierKeys.Control && !_immersiveMode)
        {
            var index = e.Key switch
            {
                >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
                >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
                _ => -1
            };
            if (index >= 0 && index < _navActions.Count)
            {
                e.Handled = true;
                var action = _navActions[index];
                Dispatcher.InvokeAsync(action);
                return;
            }
        }

        // Alt+← / Alt+→ — назад/вперёд (в веб-разделах — по истории страницы).
        if (e.Key == Key.System && Keyboard.Modifiers == ModifierKeys.Alt &&
            (e.SystemKey == Key.Left || e.SystemKey == Key.Right))
        {
            e.Handled = true;
            if (e.SystemKey == Key.Left)
            {
                if (BackButton.IsEnabled) Back_Click(BackButton, new RoutedEventArgs());
            }
            else if (ForwardButton.IsEnabled)
            {
                Forward_Click(ForwardButton, new RoutedEventArgs());
            }

            return;
        }

        if (_currentViewKey != "manga" && !_immersiveMode)
        {
            return;
        }

        if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            var enable = !_immersiveMode;
            Dispatcher.InvokeAsync(() => SetImmersiveMode(enable));
        }
        else if (e.Key == Key.Escape && _immersiveMode && !_immersiveByPage)
        {
            // Не помечаем Handled: Esc всё равно дойдёт до страницы (закрыть меню сайта и т.п.).
            Dispatcher.InvokeAsync(() => SetImmersiveMode(false));
        }
    }

    /// <summary>
    /// Режим чтения: скрываем заголовок окна и боковое меню, окно занимает весь монитор.
    /// Выход — F11, Esc или кнопка на панели браузера; прежнее состояние окна восстанавливается.
    /// </summary>
    private void SetImmersiveMode(bool enabled)
    {
        if (enabled == _immersiveMode)
        {
            return;
        }

        _immersiveMode = enabled;
        if (enabled)
        {
            _stateBeforeImmersive = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            _rootBorderBeforeImmersive = RootChromeBorder.BorderThickness;
            TitleBarBorder.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(0);
            SidebarChromeBorder.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
            RootChromeBorder.BorderThickness = new Thickness(0);
            WindowBoundsService.SetFullScreen(this, true);
            if (WindowState != WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }

            _mangaWebApp?.SetToolbarVisible(true);
        }
        else
        {
            _immersiveByPage = false;
            WindowBoundsService.SetFullScreen(this, false);
            TitleBarBorder.Visibility = Visibility.Visible;
            TitleBarRow.Height = new GridLength(40);
            SidebarChromeBorder.Visibility = Visibility.Visible;
            RootChromeBorder.BorderThickness = _rootBorderBeforeImmersive;
            _compactSidebarApplied = null;
            UpdateResponsiveSidebar();
            if (WindowState != _stateBeforeImmersive)
            {
                WindowState = _stateBeforeImmersive;
            }

            _mangaWebApp?.SetToolbarVisible(false);
        }

        _mangaWebApp?.SetReaderMode(enabled);
        WebReaderButton.Content = MakeIcon(enabled ? "fullscreen-exit" : "fullscreen", 16);
        SyncMaximizeButtonIcon();
    }

    private void UseWebContentLayout()
    {
        ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ContentScrollViewer.Padding = new Thickness(0);
        ContentScrollViewer.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        ContentScrollViewer.VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch;
        ContentHost.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        ContentHost.VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch;
    }

    private void UseStandardContentLayout()
    {
        ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        ContentScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ContentScrollViewer.Padding = new Thickness(24, 16, 24, 18);
        ContentScrollViewer.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        ContentScrollViewer.VerticalContentAlignment = System.Windows.VerticalAlignment.Top;
        ContentHost.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        ContentHost.VerticalContentAlignment = System.Windows.VerticalAlignment.Top;
    }

    private void WebNavigationStateChanged(object? sender, EventArgs e)
    {
        UpdateNavigationButtons();
    }

    private void WebZoomChanged(object? sender, EventArgs e)
    {
        UpdateWebTools();
        if (sender is not IWebZoomHost source || source.ZoomPercent == _settings.WebAppZoomPercent)
        {
            return;
        }

        _settings.WebAppZoomPercent = source.ZoomPercent;
        _settingsStore.Save(_settings);

        if (!ReferenceEquals(source, _messengerWebApps) && _messengerWebApps is not null)
        {
            _messengerWebApps.ZoomPercent = source.ZoomPercent;
        }

        if (!ReferenceEquals(source, _musicWebApp) && _musicWebApp is not null)
        {
            _musicWebApp.ZoomPercent = source.ZoomPercent;
        }

        if (!ReferenceEquals(source, _tikTokWebApp) && _tikTokWebApp is not null)
        {
            _tikTokWebApp.ZoomPercent = source.ZoomPercent;
        }

        // MangaLib намеренно не синхронизируется: у читалки свой масштаб (MangaZoomChanged).
    }

    private void UpdateNavigationButtons()
    {
        var webNavigation = CurrentWebNavigation;
        BackButton.IsEnabled = webNavigation?.CanGoBack == true || _backStack.Count > 0;
        ForwardButton.IsEnabled = webNavigation?.CanGoForward == true || _forwardStack.Count > 0;
        UpdateWebTools();
    }

    private IWebToolsHost? CurrentWebTools => CurrentWebNavigation as IWebToolsHost;

    /// <summary>Группа веб-инструментов в заголовке видна только в веб-разделах.</summary>
    private void UpdateWebTools()
    {
        var tools = CurrentWebTools;
        WebToolsPanel.Visibility = tools is null ? Visibility.Collapsed : Visibility.Visible;
        WebReaderButton.Visibility = _currentViewKey == "manga" ? Visibility.Visible : Visibility.Collapsed;
        if (tools is not null)
        {
            WebZoomText.Text = $"{tools.ZoomPercent}%";
        }
    }

    private void WebZoomOut_Click(object sender, RoutedEventArgs e) => CurrentWebTools?.ZoomOut();

    private void WebZoomIn_Click(object sender, RoutedEventArgs e) => CurrentWebTools?.ZoomIn();

    private void WebZoomReset_Click(object sender, MouseButtonEventArgs e) => CurrentWebTools?.ResetZoom();

    private void WebHome_Click(object sender, RoutedEventArgs e) => CurrentWebTools?.GoHome();

    private void WebReload_Click(object sender, RoutedEventArgs e) => CurrentWebTools?.Reload();

    private void WebReader_Click(object sender, RoutedEventArgs e) => SetImmersiveMode(!_immersiveMode);

    private void DisposeWebApps()
    {
        if (_messengerWebApps is not null)
        {
            _messengerWebApps.NavigationStateChanged -= WebNavigationStateChanged;
            _messengerWebApps.ZoomPercentChanged -= WebZoomChanged;
            _messengerWebApps.Dispose();
            _messengerWebApps = null;
        }

        if (_musicWebApp is not null)
        {
            _musicWebApp.NavigationStateChanged -= WebNavigationStateChanged;
            _musicWebApp.ZoomPercentChanged -= WebZoomChanged;
            _musicWebApp.Dispose();
            _musicWebApp = null;
        }

        if (_tikTokWebApp is not null)
        {
            _tikTokWebApp.NavigationStateChanged -= WebNavigationStateChanged;
            _tikTokWebApp.ZoomPercentChanged -= WebZoomChanged;
            _tikTokWebApp.Dispose();
            _tikTokWebApp = null;
        }

        if (_mangaWebApp is not null)
        {
            _mangaWebApp.NavigationStateChanged -= WebNavigationStateChanged;
            _mangaWebApp.ZoomPercentChanged -= MangaZoomChanged;
            _mangaWebApp.PageUrlChanged -= MangaPageUrlChanged;
            _mangaWebApp.ReaderModeToggleRequested -= MangaReaderModeToggleRequested;
            _mangaWebApp.FullScreenElementChanged -= MangaFullScreenElementChanged;
            _mangaWebApp.Dispose();
            _mangaWebApp = null;
        }
    }
}
