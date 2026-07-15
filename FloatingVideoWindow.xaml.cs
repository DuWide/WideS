using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace DevCockpit;

public partial class FloatingVideoWindow : Window
{
    private bool _allowClose;
    private VideoBrowserView? _view;

    public event EventHandler? CloseRequested;

    public FloatingVideoWindow()
    {
        InitializeComponent();
    }

    public void Attach(VideoBrowserView view)
    {
        _view = view;
        view.AttachTo(PlayerHost);
    }

    public void Detach()
    {
        _view = null;
        PlayerHost.Content = null;
    }

    public void EnterFullScreen()
    {
        Topmost = true;
        WindowState = WindowState.Maximized;
    }

    public void ExitFullScreen()
    {
        WindowState = WindowState.Normal;
        Width = 540;
        Height = 332;
    }

    public void ClosePermanently()
    {
        Detach();
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }

    private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
