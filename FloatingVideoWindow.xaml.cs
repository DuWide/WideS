using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace DevCockpit;

public partial class FloatingVideoWindow : Window
{
    private bool _allowClose;

    public event EventHandler? CloseRequested;

    public FloatingVideoWindow()
    {
        InitializeComponent();
    }

    public void Attach(VideoBrowserView view) => view.AttachTo(PlayerHost);

    public void Detach() => PlayerHost.Content = null;

    public void EnterFullScreen()
    {
        Topmost = true;
        WindowState = WindowState.Maximized;
    }

    public void ExitFullScreen()
    {
        WindowState = WindowState.Normal;
        Width = 540;
        Height = 304;
    }

    public void ClosePermanently()
    {
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
