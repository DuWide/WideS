using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfPath = System.Windows.Shapes.Path;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DevCockpit;

public partial class FloatingDockWindow : Window
{
    private readonly MediaService _mediaService;
    private readonly Action _onNewTask;
    private readonly Action _onNewNote;
    private readonly Action _onDropZone;
    private readonly Action _onOpenMain;
    private readonly Action<string[]> _onFilesDropped;
    private readonly DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IReadOnlyList<ConnectionItem> _connections = [];
    private Action<ConnectionItem>? _onConnect;
    private Action<ConnectionItem>? _onShowPassword;
    private string _dockPosition = "Center";
    private bool _autoHide;
    private bool _showMedia = true;

    public FloatingDockWindow(
        MediaService mediaService,
        Action onNewTask,
        Action onNewNote,
        Action onDropZone,
        Action onOpenMain,
        Action<string[]> onFilesDropped)
    {
        InitializeComponent();
        _mediaService = mediaService;
        _onNewTask = onNewTask;
        _onNewNote = onNewNote;
        _onDropZone = onDropZone;
        _onOpenMain = onOpenMain;
        _onFilesDropped = onFilesDropped;

        TaskButton.Content = DockContent("task", "Задача");
        NoteButton.Content = DockContent("note", "Заметка");
        DropButton.Content = DockContent("drop", "DropZone");
        OpenButton.Content = DockContent("open", "Открыть");
        ConnButton.Content = DockContent("connection", "RDP");
        DockNpPrev.Content = MakeIcon("prev", 14);
        DockNpPlay.Content = MakeIcon("play", 15);
        DockNpNext.Content = MakeIcon("next", 14);
        HideButton.Content = MakeIcon("close", 14);

        DragOver += FloatingDockWindow_DragOver;
        Drop += FloatingDockWindow_Drop;
        Deactivated += (_, _) => { if (_autoHide) HideDock(); };
        Loaded += (_, _) => PositionDock();

        _mediaTimer.Tick += async (_, _) => await UpdateNowPlaying();
    }

    public void Configure(string dockPosition, bool autoHide, bool showMedia = true)
    {
        _dockPosition = dockPosition;
        _autoHide = autoHide;
        _showMedia = showMedia;
    }

    public void SetConnections(
        IReadOnlyList<ConnectionItem> connections,
        Action<ConnectionItem> onConnect,
        Action<ConnectionItem> onShowPassword)
    {
        _connections = connections;
        _onConnect = onConnect;
        _onShowPassword = onShowPassword;
        ConnCombo.ItemsSource = connections;
        ConnCombo.DisplayMemberPath = nameof(ConnectionItem.Name);
        if (connections.Count > 0)
        {
            ConnCombo.SelectedIndex = 0;
        }
    }

    public async void ShowDock()
    {
        Opacity = 0;
        Show();
        PositionDock();
        Activate();
        _mediaTimer.Start();
        await UpdateNowPlaying();
        var anim = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(150));
        BeginAnimation(OpacityProperty, anim);
    }

    private void FloatingDockWindow_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = System.Windows.DragDropEffects.Copy;
        Root.BorderBrush = (WpfBrush)FindResource("AccentBrush");
    }

    private void PositionDock()
    {
        var area = SystemParameters.WorkArea;
        if (_dockPosition.Equals("Tray", StringComparison.OrdinalIgnoreCase))
        {
            Left = area.Right - ActualWidth - 18;
            Top = area.Bottom - ActualHeight - 18;
        }
        else
        {
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Bottom - ActualHeight - 18;
        }
    }

    public void HideDock()
    {
        _mediaTimer.Stop();
        Hide();
    }

    public bool IsShown => IsVisible;

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 18;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !IsInsideButton(e.OriginalSource as DependencyObject))
        {
            DragMove();
        }
    }

    private void FloatingDockWindow_Drop(object sender, System.Windows.DragEventArgs e)
    {
        Root.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 86, 107));
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
        if (files.Length > 0) _onFilesDropped(files);
    }

    private void Task_Click(object sender, RoutedEventArgs e) => _onNewTask();
    private void Note_Click(object sender, RoutedEventArgs e) => _onNewNote();
    private void Drop_Click(object sender, RoutedEventArgs e) => _onDropZone();
    private void Open_Click(object sender, RoutedEventArgs e) => _onOpenMain();
    private void Hide_Click(object sender, RoutedEventArgs e) => HideDock();

    private void Conn_Click(object sender, RoutedEventArgs e)
    {
        if (_connections.Count == 0)
        {
            _onOpenMain();
            return;
        }

        ConnPopup.PlacementTarget = ConnButton;
        ConnPopup.IsOpen = true;
    }

    private ConnectionItem? SelectedConnection =>
        ConnCombo.SelectedItem as ConnectionItem;

    private void ConnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConnection is not ConnectionItem connection) return;
        ConnPopup.IsOpen = false;
        _onConnect?.Invoke(connection);
    }

    private void ConnPassword_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConnection is not ConnectionItem connection) return;
        ConnPopup.IsOpen = false;
        _onShowPassword?.Invoke(connection);
    }

    private async void NpPlay_Click(object sender, RoutedEventArgs e)
    {
        await _mediaService.TogglePlayPauseAsync();
        await Task.Delay(250);
        await UpdateNowPlaying();
    }

    private async void NpNext_Click(object sender, RoutedEventArgs e)
    {
        await _mediaService.NextAsync();
        await Task.Delay(400);
        await UpdateNowPlaying();
    }

    private async void NpPrev_Click(object sender, RoutedEventArgs e)
    {
        await _mediaService.PreviousAsync();
        await Task.Delay(400);
        await UpdateNowPlaying();
    }

    private async Task UpdateNowPlaying()
    {
        if (!_showMedia)
        {
            MediaPanel.Visibility = Visibility.Collapsed;
            MediaSeparator.Visibility = Visibility.Collapsed;
            return;
        }

        var snap = await _mediaService.GetSnapshotAsync();
        var visible = snap.HasSession ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = visible;
        MediaSeparator.Visibility = visible;
        if (!snap.HasSession) return;

        UiHelpers.SetAlbumArt(DockAlbumArt, snap.AlbumArt);
        DockNpTitle.Text = snap.Title;
        DockNpArtist.Text = string.IsNullOrWhiteSpace(snap.Source)
            ? snap.Artist
            : $"{snap.Artist} · {snap.Source}".Trim(' ', '·');
        DockNpPlay.Content = MakeIcon(snap.IsPlaying ? "pause" : "play", 15);
    }

    private FrameworkElement DockContent(string icon, string label)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(MakeIcon(icon, 20));
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = (WpfBrush)FindResource("MutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        });
        return stack;
    }

    private FrameworkElement MakeIcon(string iconName, double size)
    {
        var geometry = iconName switch
        {
            "task" => "M9 16.2l-3.5-3.5L4 14.2l5 5 11-11-1.4-1.4L9 16.2z",
            "note" => "M4 4h12l4 4v12H4V4z M15 5v4h4 M7 12h10v2H7v-2z M7 15h7v2H7v-2z",
            "drop" => "M12 2l6 8a6 6 0 1 1-12 0l6-8z",
            "open" => "M3 3h8v2H5v14h14v-6h2v8H3V3z M14 3h7v7h-2V6.4l-8.3 8.3-1.4-1.4L17.6 5H14V3z",
            "connection" => "M4 6h16v12H4V6z M8 10h8v2H8v-2z M12 2v4 M8 6l4-4 4 4",
            "play" => "M8 5v14l11-7L8 5z",
            "pause" => "M6 5h4v14H6V5z M14 5h4v14h-4V5z",
            "prev" => "M6 6h2v12H6V6z M20 6v12l-9-6 9-6z",
            "next" => "M16 6h2v12h-2V6z M4 6l9 6-9 6V6z",
            "close" => "M6.4 5L5 6.4 10.6 12 5 17.6 6.4 19 12 13.4 17.6 19 19 17.6 13.4 12 19 6.4 17.6 5 12 10.6 6.4 5z",
            _ => "M4 4h16v16H4V4z"
        };
        return new WpfPath
        {
            Data = Geometry.Parse(geometry),
            Fill = (WpfBrush)FindResource("TextBrush"),
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size
        };
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is WpfButton) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
