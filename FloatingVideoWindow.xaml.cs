using System.ComponentModel;
using System.Windows;

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
        if (_view is not null)
        {
            _view.DragRequested -= HandleDragRequested;
        }

        _view = view;
        _view.DragRequested += HandleDragRequested;
        view.AttachTo(PlayerHost);
    }

    public void Detach()
    {
        if (_view is not null)
        {
            _view.DragRequested -= HandleDragRequested;
            _view = null;
        }

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
        Height = 304;
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

    private void HandleDragRequested()
    {
        try
        {
            DragMove();
        }
        catch
        {
            // DragMove может упасть, если кнопку уже отпустили.
        }
    }
}
