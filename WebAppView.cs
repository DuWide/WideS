using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WpfButton = System.Windows.Controls.Button;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace DevCockpit;

public interface IWebNavigationHost
{
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    void GoBack();
    void GoForward();
    event EventHandler? NavigationStateChanged;
}

public interface IWebZoomHost
{
    int ZoomPercent { get; set; }
    event EventHandler? ZoomPercentChanged;
}

/// <summary>Кнопки масштаба, «Домой» и «Обновить» для группы веб-инструментов в заголовке окна.</summary>
public interface IWebToolsHost : IWebZoomHost
{
    void ZoomIn();
    void ZoomOut();
    void ResetZoom();
    void GoHome();
    void Reload();
}

/// <param name="UserScript">Скрипт, который внедряется в каждую страницу сервиса (например, горячие клавиши читалки).</param>
public sealed record WebAppDefinition(
    string Title,
    Uri HomeUri,
    IReadOnlyCollection<string> AllowedDomains,
    string? UserScript = null);

public sealed class WebAppView : WpfUserControl, IWebNavigationHost, IWebToolsHost, IDisposable
{
    private const int MinZoomPercent = 50;
    private const int MaxZoomPercent = 150;

    private readonly WebAppDefinition _definition;
    private readonly WebView2 _webView = new();
    private readonly Grid _browserLayer = new();
    private readonly Border _loadingPanel;
    private readonly Border _errorPanel;
    private readonly TextBlock _errorText;
    private readonly TextBlock _zoomLabel = new();
    private readonly bool _enableReaderMode;
    private FrameworkElement? _toolbar;
    private WpfButton? _readerButton;
    private int _zoomPercent;
    private Task? _initializationTask;
    private bool _disposed;

    public WebAppView(WebAppDefinition definition, bool showToolbar = true, bool enableReaderMode = false)
    {
        _definition = definition;
        _enableReaderMode = enableReaderMode;
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        if (showToolbar)
        {
            var toolbar = BuildToolbar();
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);
            _toolbar = toolbar;
        }

        _browserLayer.Children.Add(_webView);
        // Тонкая полоса загрузки в отдельной строке над страницей: поверх WebView2 (HWND) WPF рисовать не может.
        _loadingPanel = BuildLoadingPanel();
        Grid.SetRow(_loadingPanel, 1);
        root.Children.Add(_loadingPanel);
        (_errorPanel, _errorText) = BuildErrorPanel();
        _browserLayer.Children.Add(_errorPanel);
        Grid.SetRow(_browserLayer, 2);
        root.Children.Add(_browserLayer);

        Content = root;
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public event EventHandler? NavigationStateChanged;
    public event EventHandler? ZoomPercentChanged;

    /// <summary>Адрес открытой страницы сменился (в том числе внутри одностраничного сайта).</summary>
    public event EventHandler<Uri>? PageUrlChanged;

    /// <summary>Нажата кнопка «Режим чтения» на панели инструментов.</summary>
    public event EventHandler? ReaderModeToggleRequested;

    /// <summary>Страница вошла в полноэкранный режим (Fullscreen API) или вышла из него.</summary>
    public event EventHandler<bool>? FullScreenElementChanged;

    /// <summary>Страница, которую открыть при первом запуске вместо домашней (например, последняя прочитанная глава).</summary>
    public Uri? StartUri { get; set; }

    /// <summary>Текущий адрес страницы или null, если браузер ещё не запущен.</summary>
    public Uri? CurrentUri =>
        Uri.TryCreate(_webView.CoreWebView2?.Source, UriKind.Absolute, out var uri) ? uri : null;

    public void SetReaderMode(bool enabled)
    {
        if (_readerButton is null)
        {
            return;
        }

        _readerButton.Content = UiIconFactory.Create(enabled ? "fullscreen-exit" : "fullscreen", 13);
        _readerButton.ToolTip = enabled ? "Выйти из режима чтения (F11 или Esc)" : "Режим чтения (F11)";
    }

    public void SetToolbarVisible(bool visible)
    {
        if (_toolbar is not null)
        {
            _toolbar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public int ZoomPercent
    {
        get => _zoomPercent;
        set => ApplyZoom(value, notify: false);
    }

    public bool CanGoBack => _webView.CoreWebView2?.CanGoBack == true;
    public bool CanGoForward => _webView.CoreWebView2?.CanGoForward == true;

    public void GoBack()
    {
        if (CanGoBack)
        {
            _webView.CoreWebView2.GoBack();
        }
    }

    public void GoForward()
    {
        if (CanGoForward)
        {
            _webView.CoreWebView2.GoForward();
        }
    }

    public Task EnsureInitializedAsync()
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        return _initializationTask ??= InitializeAsync();
    }

    public void GoHome()
    {
        if (_webView.CoreWebView2 is null)
        {
            _ = EnsureInitializedAsync();
            return;
        }

        _webView.CoreWebView2.Navigate(_definition.HomeUri.AbsoluteUri);
    }

    public void Reload()
    {
        if (_webView.CoreWebView2 is null)
        {
            _ = EnsureInitializedAsync();
            return;
        }

        HideError();
        _loadingPanel.Visibility = Visibility.Visible;
        _webView.CoreWebView2.Reload();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loaded -= OnLoaded;
        IsVisibleChanged -= OnIsVisibleChanged;
        _webView.ZoomFactorChanged -= WebView_ZoomFactorChanged;
        _webView.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyZoom(_zoomPercent, notify: false);
            await EnsureInitializedAsync();
        }
        catch
        {
            // InitializeAsync already shows a useful error inside the view.
        }
    }

    /// <summary>
    /// Скрытые веб-приложения (другая вкладка WideS, окно в трее) переводим в режим экономии памяти:
    /// WebView2 сбрасывает кэши и меньше нагружает слабые ноутбуки. При показе — обычный режим.
    /// </summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var core = _webView.CoreWebView2;
        if (core is null || _disposed)
        {
            return;
        }

        try
        {
            core.MemoryUsageTargetLevel = IsVisible
                ? CoreWebView2MemoryUsageTargetLevel.Normal
                : CoreWebView2MemoryUsageTargetLevel.Low;
        }
        catch
        {
            // Старый WebView2 Runtime без поддержки — просто работаем как раньше.
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            _loadingPanel.Visibility = Visibility.Visible;
            HideError();
            var environment = await WebViewEnvironmentProvider.GetAsync();
            if (_disposed)
            {
                return;
            }

            await _webView.EnsureCoreWebView2Async(environment);
            if (_disposed)
            {
                return;
            }

            ConfigureCore(_webView.CoreWebView2);
            if (!string.IsNullOrWhiteSpace(_definition.UserScript))
            {
                try
                {
                    await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(_definition.UserScript);
                }
                catch
                {
                    // Без скрипта сайт всё равно работает, просто без дополнительных клавиш.
                }
            }
            _webView.ZoomFactor = _zoomPercent / 100d;
            _webView.ZoomFactorChanged += WebView_ZoomFactorChanged;
            var start = StartUri is not null && IsAllowedServiceUri(StartUri.AbsoluteUri)
                ? StartUri
                : _definition.HomeUri;
            _webView.CoreWebView2.Navigate(start.AbsoluteUri);
        }
        catch (Exception ex)
        {
            _initializationTask = null;
            ShowError($"Не удалось запустить встроенный браузер.\n{ex.Message}");
            throw;
        }
    }

    private void ConfigureCore(CoreWebView2 core)
    {
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.NavigationStarting += Core_NavigationStarting;
        core.NavigationCompleted += Core_NavigationCompleted;
        core.NewWindowRequested += Core_NewWindowRequested;
        core.ProcessFailed += Core_ProcessFailed;
        core.HistoryChanged += Core_HistoryChanged;
        core.SourceChanged += Core_SourceChanged;
        core.ContainsFullScreenElementChanged += Core_ContainsFullScreenElementChanged;
    }

    private void Core_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var uri = CurrentUri;
        if (uri is not null && IsAllowedServiceUri(uri.AbsoluteUri))
        {
            PageUrlChanged?.Invoke(this, uri);
        }
    }

    private void Core_ContainsFullScreenElementChanged(object? sender, object e)
    {
        FullScreenElementChanged?.Invoke(this, _webView.CoreWebView2?.ContainsFullScreenElement == true);
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsAllowedServiceUri(e.Uri))
        {
            HideError();
            _loadingPanel.Visibility = Visibility.Visible;
            return;
        }

        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _loadingPanel.Visibility = Visibility.Collapsed;
        if (e.IsSuccess)
        {
            HideError();
        }
        else
        {
            ShowError($"Страница не загрузилась ({e.WebErrorStatus}). Проверьте подключение к интернету.");
        }

        NavigationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (IsAllowedServiceUri(e.Uri))
        {
            _webView.CoreWebView2.Navigate(e.Uri);
            return;
        }

        OpenExternal(e.Uri);
    }

    private void Core_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        ShowError($"Встроенный браузер остановлен ({e.ProcessFailedKind}). Нажмите «Повторить».");
        NavigationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Core_HistoryChanged(object? sender, object e)
    {
        NavigationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsAllowedServiceUri(string? value)
    {
        if (string.Equals(value, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _definition.AllowedDomains.Any(domain =>
            uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenExternal(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // The popup stays blocked if Windows cannot open its target safely.
        }
    }

    private DockPanel BuildToolbar()
    {
        var toolbar = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(8, 5, 8, 5)
        };

        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        actions.Children.Add(ToolbarButton("minus", "Мельче (Ctrl + колесо мыши)", () => StepZoom(-10)));

        _zoomLabel.FontSize = 10;
        _zoomLabel.MinWidth = 32;
        _zoomLabel.TextAlignment = TextAlignment.Center;
        _zoomLabel.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        _zoomLabel.Margin = new Thickness(4, 0, 4, 0);
        _zoomLabel.ToolTip = "Сбросить масштаб";
        _zoomLabel.Cursor = System.Windows.Input.Cursors.Hand;
        _zoomLabel.MouseLeftButtonUp += (_, _) => ApplyZoom(DefaultZoomPercent(), notify: true);
        _zoomLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        actions.Children.Add(_zoomLabel);

        actions.Children.Add(ToolbarButton("plus", "Крупнее (Ctrl + колесо мыши)", () => StepZoom(10)));
        actions.Children.Add(ToolbarButton("home", "Домой", GoHome));
        actions.Children.Add(ToolbarButton("reload", "Обновить", Reload));
        if (_enableReaderMode)
        {
            _readerButton = ToolbarButton("fullscreen", "Режим чтения (F11)",
                () => ReaderModeToggleRequested?.Invoke(this, EventArgs.Empty));
            actions.Children.Add(_readerButton);
        }

        DockPanel.SetDock(actions, Dock.Right);
        toolbar.Children.Add(actions);

        toolbar.Children.Add(new Border());
        return toolbar;
    }

    private static WpfButton ToolbarButton(string icon, string tooltip, Action action)
    {
        var button = new WpfButton
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(0),
            Content = UiIconFactory.Create(icon, 13),
            ToolTip = tooltip
        };
        button.SetResourceReference(StyleProperty, "GhostButton");
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>
    /// Индикатор загрузки — полоса 2 px цвета акцента, «бегущий» отрезок. Анимация идёт только
    /// пока полоса реально видна (IsVisible), чтобы не нагружать рендер в фоне.
    /// </summary>
    private static Border BuildLoadingPanel()
    {
        var runner = new Border
        {
            Width = 160,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(1),
            RenderTransform = new TranslateTransform()
        };
        runner.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        var track = new Grid { ClipToBounds = true, Height = 2 };
        track.Children.Add(runner);
        var panel = new Border { Child = track, Height = 2, ToolTip = "Загрузка…" };

        panel.IsVisibleChanged += (_, _) =>
        {
            var transform = (TranslateTransform)runner.RenderTransform;
            if (!panel.IsVisible)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                return;
            }

            var width = Math.Max(track.ActualWidth, 400);
            var animation = new System.Windows.Media.Animation.DoubleAnimation(-runner.Width, width, TimeSpan.FromSeconds(1.1))
            {
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        };
        return panel;
    }

    private (Border Panel, TextBlock Text) BuildErrorPanel()
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 620,
            Margin = new Thickness(0, 0, 0, 16)
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var retry = new WpfButton
        {
            Content = "Повторить",
            MinWidth = 110,
            Height = 36,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        retry.SetResourceReference(StyleProperty, "PrimaryButton");
        retry.Click += (_, _) => Reload();

        var stack = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        stack.Children.Add(text);
        stack.Children.Add(retry);

        var panel = new Border
        {
            Child = stack,
            Visibility = Visibility.Collapsed
        };
        panel.SetResourceReference(BackgroundProperty, "AppBgBrush");
        return (panel, text);
    }

    public void ZoomIn() => StepZoom(10);

    public void ZoomOut() => StepZoom(-10);

    public void ResetZoom() => ApplyZoom(DefaultZoomPercent(), notify: true);

    private void StepZoom(int delta) => ApplyZoom(_zoomPercent + delta, notify: true);

    private void ApplyZoom(int percent, bool notify)
    {
        if (percent <= 0)
        {
            percent = DefaultZoomPercent();
        }

        percent = Math.Clamp(percent, MinZoomPercent, MaxZoomPercent);
        var changed = percent != _zoomPercent;
        _zoomPercent = percent;
        _zoomLabel.Text = $"{percent}%";

        if (_webView.CoreWebView2 is not null &&
            Math.Abs(_webView.ZoomFactor - percent / 100d) > 0.001)
        {
            _webView.ZoomFactor = percent / 100d;
        }

        if (changed && notify)
        {
            ZoomPercentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>При масштабе Windows выше 100% страницы иначе выглядят увеличенными.</summary>
    private int DefaultZoomPercent()
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0)
        {
            return 100;
        }

        return Math.Clamp((int)(Math.Round(100 / scale / 5) * 5), MinZoomPercent, MaxZoomPercent);
    }

    private void WebView_ZoomFactorChanged(object? sender, EventArgs e)
    {
        ApplyZoom((int)Math.Round(_webView.ZoomFactor * 100), notify: true);
    }

    private void ShowError(string message)
    {
        _loadingPanel.Visibility = Visibility.Collapsed;
        _errorText.Text = message;
        _errorPanel.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        _errorPanel.Visibility = Visibility.Collapsed;
    }
}
