using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
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

    public void RefreshTheme()
    {
        Root.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        Root.SetResourceReference(Border.BorderBrushProperty, "AccentBorderBrush");
        InvalidateVisual();
        UpdateLayout();
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
        Root.SetResourceReference(Border.BorderBrushProperty, "AccentBorderBrush");
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
        stack.Children.Add(MakeIcon(icon, 18));
        var text = new TextBlock
        {
            Text = label,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(text);
        return stack;
    }

    private FrameworkElement MakeIcon(string iconName, double size)
    {
        return UiIconFactory.Create(iconName, size);
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
