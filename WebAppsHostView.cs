using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace DevCockpit;

public static class WebAppDefinitions
{
    public static WebAppDefinition WhatsApp { get; } = new(
        "WhatsApp",
        new Uri("https://web.whatsapp.com/"),
        ["whatsapp.com"]);

    public static WebAppDefinition Telegram { get; } = new(
        "Telegram",
        new Uri("https://web.telegram.org/"),
        ["telegram.org"]);

    public static WebAppDefinition YouTubeMusic { get; } = new(
        "YouTube Music",
        new Uri("https://music.youtube.com/"),
        ["youtube.com", "google.com", "googleusercontent.com"]);

    public static WebAppDefinition TikTok { get; } = new(
        "TikTok",
        new Uri("https://www.tiktok.com/"),
        ["tiktok.com", "tiktokv.com", "ttlivecdn.com", "bytedance.com", "byteoversea.com"]);

    /// <summary>
    /// MangaLib: сам сайт, вход через auth.lib.social, API и картинки на CDN, а также страницы входа
    /// через соцсети (VK, Яндекс, Google, Discord, Telegram, Mail.ru) — иначе OAuth ушёл бы во внешний браузер
    /// и cookie не сохранились бы во встроенном профиле.
    /// </summary>
    public static WebAppDefinition MangaLib { get; } = new(
        "MangaLib",
        new Uri("https://mangalib.me/ru"),
        [
            "mangalib.me", "mangalib.org", "lib.social", "cdnlibs.org", "imglib.info",
            "oauth.vk.com", "id.vk.com", "id.vk.ru", "login.vk.com",
            "oauth.yandex.ru", "passport.yandex.ru", "oauth.yandex.com", "passport.yandex.com",
            "accounts.google.com", "accounts.youtube.com", "discord.com", "oauth.telegram.org", "oauth.mail.ru"
        ],
        MangaReaderKeysScript);

    /// <summary>
    /// Листание в читалке MangaLib с клавиатуры. A/D (и Ф/В в русской раскладке — берём физическую клавишу)
    /// превращаются в ←/→, которые понимает сама читалка. Если сайт стрелку не обработал (вертикальный режим
    /// для манхвы), ←/→ и W/S прокручивают страницу на ~экран. Space/PageUp/PageDown остаются штатными.
    /// В полях ввода (поиск, комментарии) и с Ctrl/Alt ничего не перехватывается.
    /// </summary>
    private const string MangaReaderKeysScript = """
        (() => {
          if (window.__widesReaderKeys) return;
          window.__widesReaderKeys = true;
          const editable = el => !!el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName));
          const inReader = () => /\/read(\/|$)/.test(location.pathname);
          const scrollPage = dir => {
            const el = document.scrollingElement || document.documentElement;
            el.scrollBy({ top: dir * Math.round(window.innerHeight * 0.85), behavior: 'smooth' });
          };
          window.addEventListener('keydown', e => {
            if (!inReader() || e.ctrlKey || e.altKey || e.metaKey || e.repeat && e.code.startsWith('Key')) return;
            if (editable(e.target) || editable(document.activeElement)) return;
            if (e.code === 'KeyD' || e.code === 'KeyA') {
              const next = e.code === 'KeyD';
              e.preventDefault();
              const key = next ? 'ArrowRight' : 'ArrowLeft';
              (document.activeElement || document.body).dispatchEvent(new KeyboardEvent('keydown', {
                key, code: key, keyCode: next ? 39 : 37, which: next ? 39 : 37, bubbles: true, cancelable: true }));
              return;
            }
            if (e.code === 'KeyS' || e.code === 'KeyW') {
              e.preventDefault();
              scrollPage(e.code === 'KeyS' ? 1 : -1);
            }
          }, true);
          window.addEventListener('keydown', e => {
            if (e.defaultPrevented || !inReader() || e.ctrlKey || e.altKey || e.metaKey) return;
            if (editable(e.target) || editable(document.activeElement)) return;
            if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
              scrollPage(e.key === 'ArrowRight' ? 1 : -1);
            }
          });
        })();
        """;
}

public sealed class MessengerWebAppsView : WpfUserControl, IWebNavigationHost, IWebToolsHost, IDisposable
{
    private readonly WebAppView _whatsApp = new(WebAppDefinitions.WhatsApp, showToolbar: false);
    private readonly WebAppView _telegram = new(WebAppDefinitions.Telegram, showToolbar: false);
    private readonly WpfButton _whatsAppTab;
    private readonly WpfButton _telegramTab;
    private readonly TextBlock _zoomLabel = new();
    private WebAppView _activeView;
    private bool _disposed;

    public MessengerWebAppsView()
    {
        _activeView = _whatsApp;
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var tabs = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        _whatsAppTab = TabButton("WhatsApp", _whatsApp);
        _telegramTab = TabButton("Telegram", _telegram);
        tabs.Children.Add(_whatsAppTab);
        tabs.Children.Add(_telegramTab);

        var bar = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(8, 5, 8, 5)
        };
        // Масштаб, «Домой» и «Обновить» теперь в заголовке окна (группа веб-инструментов WideS).
        bar.Children.Add(tabs);
        root.Children.Add(bar);

        var views = new Grid();
        views.Children.Add(_whatsApp);
        views.Children.Add(_telegram);
        _telegram.Visibility = Visibility.Collapsed;
        Grid.SetRow(views, 1);
        root.Children.Add(views);

        Content = root;
        _whatsApp.NavigationStateChanged += ChildNavigationStateChanged;
        _telegram.NavigationStateChanged += ChildNavigationStateChanged;
        _whatsApp.ZoomPercentChanged += ChildZoomChanged;
        _telegram.ZoomPercentChanged += ChildZoomChanged;
        Loaded += MessengerWebAppsView_Loaded;
        UpdateTabs();
        UpdateZoomLabel();
    }

    public event EventHandler? NavigationStateChanged;
    public event EventHandler? ZoomPercentChanged;

    public int ZoomPercent
    {
        get => _activeView.ZoomPercent;
        set
        {
            _whatsApp.ZoomPercent = value;
            _telegram.ZoomPercent = value;
            UpdateZoomLabel();
        }
    }

    public bool CanGoBack => _activeView.CanGoBack;
    public bool CanGoForward => _activeView.CanGoForward;

    public void GoBack() => _activeView.GoBack();
    public void GoForward() => _activeView.GoForward();
    public void ZoomIn() => _activeView.ZoomIn();
    public void ZoomOut() => _activeView.ZoomOut();
    public void ResetZoom() => _activeView.ResetZoom();
    public void GoHome() => _activeView.GoHome();
    public void Reload() => _activeView.Reload();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loaded -= MessengerWebAppsView_Loaded;
        _whatsApp.NavigationStateChanged -= ChildNavigationStateChanged;
        _telegram.NavigationStateChanged -= ChildNavigationStateChanged;
        _whatsApp.ZoomPercentChanged -= ChildZoomChanged;
        _telegram.ZoomPercentChanged -= ChildZoomChanged;
        _whatsApp.Dispose();
        _telegram.Dispose();
    }

    private async void MessengerWebAppsView_Loaded(object sender, RoutedEventArgs e)
    {
        await Task.WhenAll(
            InitializeWithoutThrowingAsync(_whatsApp),
            InitializeWithoutThrowingAsync(_telegram));
    }

    private static async Task InitializeWithoutThrowingAsync(WebAppView view)
    {
        try
        {
            await view.EnsureInitializedAsync();
        }
        catch
        {
            // Each child renders its own initialization error.
        }
    }

    private WpfButton TabButton(string title, WebAppView view)
    {
        var button = new WpfButton
        {
            Content = title,
            MinWidth = 92,
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 3, 10, 3)
        };
        button.Click += (_, _) => Activate(view);
        return button;
    }

    private void Activate(WebAppView view)
    {
        _activeView = view;
        _whatsApp.Visibility = view == _whatsApp ? Visibility.Visible : Visibility.Collapsed;
        _telegram.Visibility = view == _telegram ? Visibility.Visible : Visibility.Collapsed;
        UpdateTabs();
        UpdateZoomLabel();
        NavigationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTabs()
    {
        _whatsAppTab.SetResourceReference(
            StyleProperty,
            _activeView == _whatsApp ? "PrimaryButton" : "GhostButton");
        _telegramTab.SetResourceReference(
            StyleProperty,
            _activeView == _telegram ? "PrimaryButton" : "GhostButton");
    }

    private void ChildZoomChanged(object? sender, EventArgs e)
    {
        if (sender is not WebAppView source)
        {
            return;
        }

        var other = ReferenceEquals(source, _whatsApp) ? _telegram : _whatsApp;
        other.ZoomPercent = source.ZoomPercent;
        UpdateZoomLabel();
        ZoomPercentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateZoomLabel()
    {
        _zoomLabel.Text = $"{_activeView.ZoomPercent}%";
    }

    private void ChildNavigationStateChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _activeView))
        {
            NavigationStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
