using System.Windows;
using System.Windows.Controls;

namespace DevCockpit;

public partial class MainWindow
{
    private MessengerWebAppsView? _messengerWebApps;
    private WebAppView? _musicWebApp;

    private IWebNavigationHost? CurrentWebNavigation => _currentViewKey switch
    {
        "messengers" => _messengerWebApps,
        "music" => _musicWebApp,
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
            _musicWebApp = new WebAppView(WebAppDefinitions.YouTubeMusic);
            _musicWebApp.NavigationStateChanged += WebNavigationStateChanged;
        }

        ContentHost.Content = _musicWebApp;
        UpdateNavigationButtons();
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

    private void UpdateNavigationButtons()
    {
        var webNavigation = CurrentWebNavigation;
        BackButton.IsEnabled = webNavigation?.CanGoBack == true || _backStack.Count > 0;
        ForwardButton.IsEnabled = webNavigation?.CanGoForward == true || _forwardStack.Count > 0;
    }

    private void DisposeWebApps()
    {
        if (_messengerWebApps is not null)
        {
            _messengerWebApps.NavigationStateChanged -= WebNavigationStateChanged;
            _messengerWebApps.Dispose();
            _messengerWebApps = null;
        }

        if (_musicWebApp is not null)
        {
            _musicWebApp.NavigationStateChanged -= WebNavigationStateChanged;
            _musicWebApp.Dispose();
            _musicWebApp = null;
        }
    }
}
