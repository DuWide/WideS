using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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

public sealed record WebAppDefinition(
    string Title,
    Uri HomeUri,
    IReadOnlyCollection<string> AllowedDomains);

public sealed class WebAppView : WpfUserControl, IWebNavigationHost, IDisposable
{
    private readonly WebAppDefinition _definition;
    private readonly WebView2 _webView = new();
    private readonly Grid _browserLayer = new();
    private readonly Border _loadingPanel;
    private readonly Border _errorPanel;
    private readonly TextBlock _errorText;
    private Task? _initializationTask;
    private bool _disposed;

    public WebAppView(WebAppDefinition definition)
    {
        _definition = definition;
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = BuildToolbar();
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        _browserLayer.Children.Add(_webView);
        _loadingPanel = BuildLoadingPanel();
        _browserLayer.Children.Add(_loadingPanel);
        (_errorPanel, _errorText) = BuildErrorPanel();
        _browserLayer.Children.Add(_errorPanel);
        Grid.SetRow(_browserLayer, 1);
        root.Children.Add(_browserLayer);

        Content = root;
        Loaded += OnLoaded;
    }

    public event EventHandler? NavigationStateChanged;

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
        _webView.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureInitializedAsync();
        }
        catch
        {
            // InitializeAsync already shows a useful error inside the view.
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
            _webView.CoreWebView2.Navigate(_definition.HomeUri.AbsoluteUri);
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
            Margin = new Thickness(8, 7, 8, 7)
        };

        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        actions.Children.Add(ToolbarButton("home", "Домой", GoHome));
        actions.Children.Add(ToolbarButton("reload", "Обновить", Reload));
        DockPanel.SetDock(actions, Dock.Right);
        toolbar.Children.Add(actions);

        var title = new TextBlock
        {
            Text = _definition.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        toolbar.Children.Add(title);
        return toolbar;
    }

    private static WpfButton ToolbarButton(string icon, string tooltip, Action action)
    {
        var button = new WpfButton
        {
            Width = 32,
            Height = 30,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(0),
            Content = UiIconFactory.Create(icon, 15),
            ToolTip = tooltip
        };
        button.SetResourceReference(StyleProperty, "GhostButton");
        button.Click += (_, _) => action();
        return button;
    }

    private static Border BuildLoadingPanel()
    {
        var text = new TextBlock
        {
            Text = "Загрузка…",
            FontSize = 13,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var panel = new Border { Child = text };
        panel.SetResourceReference(BackgroundProperty, "AppBgBrush");
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
