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
}

public sealed class MessengerWebAppsView : WpfUserControl, IWebNavigationHost, IDisposable
{
    private readonly WebAppView _whatsApp = new(WebAppDefinitions.WhatsApp);
    private readonly WebAppView _telegram = new(WebAppDefinitions.Telegram);
    private readonly WpfButton _whatsAppTab;
    private readonly WpfButton _telegramTab;
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

        var tabs = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(8, 6, 8, 0)
        };
        _whatsAppTab = TabButton("WhatsApp", _whatsApp);
        _telegramTab = TabButton("Telegram", _telegram);
        tabs.Children.Add(_whatsAppTab);
        tabs.Children.Add(_telegramTab);
        root.Children.Add(tabs);

        var views = new Grid();
        views.Children.Add(_whatsApp);
        views.Children.Add(_telegram);
        _telegram.Visibility = Visibility.Collapsed;
        Grid.SetRow(views, 1);
        root.Children.Add(views);

        Content = root;
        _whatsApp.NavigationStateChanged += ChildNavigationStateChanged;
        _telegram.NavigationStateChanged += ChildNavigationStateChanged;
        Loaded += MessengerWebAppsView_Loaded;
        UpdateTabs();
    }

    public event EventHandler? NavigationStateChanged;

    public bool CanGoBack => _activeView.CanGoBack;
    public bool CanGoForward => _activeView.CanGoForward;

    public void GoBack() => _activeView.GoBack();
    public void GoForward() => _activeView.GoForward();

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
            MinWidth = 112,
            Height = 34,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(14, 6, 14, 6)
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

    private void ChildNavigationStateChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _activeView))
        {
            NavigationStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
