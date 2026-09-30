using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DevCockpit;

public partial class MainWindow : Window
{
    private const int HotkeyNewNote = 1001;
    private const int HotkeyNewTask = 1002;
    private const int HotkeyDock = 1003;
    private const int HotkeySearch = 1004;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkF1 = 0x70;
    private const uint VkF2 = 0x71;
    private const uint VkK = 0x4B;
    private const uint VkSpace = 0x20;

    private readonly ProjectStore _projectStore = new();
    private readonly JsonFileStore<NotesStoreData> _notesStore = new(AppPaths.NotesJson);
    private readonly JsonFileStore<ConnectionsStoreData> _connectionsStore = new(AppPaths.ConnectionsJson);
    private readonly JsonFileStore<AppSettingsData> _settingsStore = new(AppPaths.SettingsJson);
    private readonly JsonFileStore<TasksStoreData> _tasksStore = new(AppPaths.TasksJson);
    private readonly JsonFileStore<ActivityStoreData> _activityStore = new(AppPaths.ActivityJson);
    private readonly JsonFileStore<ProjectTemplatesStoreData> _templatesStore = new(AppPaths.ProjectTemplatesJson);

    private ProjectStoreData _projects = new();
    private NotesStoreData _notes = new();
    private ConnectionsStoreData _connections = new();
    private AppSettingsData _settings = new();
    private TasksStoreData _tasks = new();
    private ActivityStoreData _activity = new();
    private ProjectTemplatesStoreData _templates = new();
    private ProjectProfile? _selectedProject;
    private ProjectProfile? _focusProject;
    private DateTime? _focusStartedAt;
    private readonly Dictionary<string, ViewDisplayMode> _viewModes = new();
    private string _viewScope = "projects";
    private string _projectDetailTab = "notes";
    private readonly Dictionary<string, WpfButton> _sideNavButtons = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeSideNavKey = "projects";
    private string _connectionTypeFilter = "Все";
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private string? _currentViewKey;
    private bool _isHistoryNavigation;
    private readonly DispatcherTimer _taskTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _pillTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly MediaService _mediaService = new();
    private readonly DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _telegramTimer = new() { Interval = TimeSpan.FromSeconds(45) };
    // Журнал пишется на диск не на каждое действие (fsync + .bak на UI-потоке), а не чаще раза в 2 с.
    private readonly DispatcherTimer _activitySaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _lastNowPlayingKey;
    private byte[]? _lastNowPlayingArt;
    private readonly TelegramTaskService _telegramTaskService = new();
    private DateTime _lastTelegramDesktopWriteUtc = DateTime.MinValue;
    private TaskReminderWindow? _activeReminderWindow;
    private Guid? _activeTaskPillId;
    private Guid? _trackedTaskId;
    private Forms.NotifyIcon? _trayIcon;
    private bool _allowExit;
    private HwndSource? _hwndSource;
    private FloatingDockWindow? _dockWindow;
    private GlobalSearchWindow? _globalSearchWindow;

    public MainWindow()
    {
        InitializeComponent();
        AppPaths.EnsureDataDirectory();
        InitializeWindowIcons();
        LoadData();
        if (WindowBoundsService.IsPlacementMaximized(_settings.MainWindowPlacement))
        {
            WindowState = WindowState.Maximized;
        }
        AfterLoadData();
        BuildNavGrouped();
        SetupTrayIcon();
        _taskTimer.Tick += (_, _) => CheckTaskReminders();
        _taskTimer.Start();
        _pillTimer.Tick += (_, _) =>
        {
            // Секундомер задачи в заголовке не нужен, пока окно в трее или свёрнуто.
            if (IsVisible && WindowState != WindowState.Minimized) UpdateActiveTaskPill();
        };
        _activitySaveTimer.Tick += (_, _) => FlushActivityLog();
        Dispatcher.ShutdownStarted += (_, _) => FlushPendingSaves();
        IsVisibleChanged += (_, _) => OnWindowVisibilityChanged();
        StateChanged += (_, _) => OnWindowVisibilityChanged();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseMove += OnGlowMouseMove;
        Loaded += (_, _) =>
        {
            SingleInstanceService.StartActivationListener(this, _ => ShowFromTray());
            CheckTaskReminders();
            UpdateActiveTaskPill();
            UpdateResponsiveSidebar();
        };
        SizeChanged += (_, _) => UpdateResponsiveSidebar();
        TaskNotificationService.NotificationActivated += OnTaskNotificationActivated;
        ShowProjects();
        InitializeNowPlaying();
        InitializeTelegramPolling();
        AddLog("OK", "WideS запущен.");
    }

    private void InitializeTelegramPolling()
    {
        _telegramTimer.Tick += async (_, _) => await PollTelegramTasksAsync();
        if (CanPollTelegram())
        {
            _telegramTimer.Start();
            _ = PollTelegramTasksAsync();
        }
    }

    private async void InitializeNowPlaying()
    {
        NpPrev.Content = MakeIcon("prev", 13);
        NpPlay.Content = MakeIcon("play", 14);
        NpNext.Content = MakeIcon("next", 13);
        try
        {
            await _mediaService.InitializeAsync();
            _mediaTimer.Tick += async (_, _) => await UpdateNowPlaying();
            _mediaTimer.Start();
            await UpdateNowPlaying();
        }
        catch
        {
            NowPlayingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task UpdateNowPlaying()
    {
        if (!WorkModeService.ShowMedia(_settings.WorkMode))
        {
            NowPlayingPanel.Visibility = Visibility.Collapsed;
            _lastNowPlayingKey = null;
            return;
        }

        // Окно в трее или свёрнуто: мини-плеер не виден — не опрашиваем систему и не перерисовываем.
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        var snap = await _mediaService.GetSnapshotAsync();
        if (!snap.HasSession)
        {
            NowPlayingPanel.Visibility = Visibility.Collapsed;
            _lastNowPlayingKey = null;
            return;
        }

        // Раньше каждые 2 с заново декодировалась обложка и пересоздавались иконки, даже без изменений.
        var key = $"{snap.Source}\n{snap.Title}\n{snap.Artist}\n{snap.IsPlaying}";
        if (key == _lastNowPlayingKey && ReferenceEquals(snap.AlbumArt, _lastNowPlayingArt) &&
            NowPlayingPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        _lastNowPlayingKey = key;
        _lastNowPlayingArt = snap.AlbumArt;
        NowPlayingPanel.Visibility = Visibility.Visible;
        NpTitle.Text = snap.Title;
        NpArtist.Text = snap.Artist;
        var source = string.IsNullOrWhiteSpace(snap.Source) ? "Сейчас играет" : snap.Source;
        NowPlayingPanel.ToolTip = string.IsNullOrWhiteSpace(snap.Artist)
            ? $"{source}: {snap.Title}"
            : $"{source}: {snap.Title} — {snap.Artist}";
        NpPlay.Content = MakeIcon(snap.IsPlaying ? "pause" : "play", 14);
        UiHelpers.SetAlbumArt(NpAlbumArt, snap.AlbumArt);
    }

    private void NowPlayingPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Клики по плееру не должны начинать перетаскивание окна.
        e.Handled = true;
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

    private void ToggleDock()
    {
        _dockWindow ??= new FloatingDockWindow(
            _mediaService,
            () => AddTask(forceCommon: true),
            () => AddNote(forceCommon: true),
            ShowFromTray);

        RefreshDockConnections();

        if (_dockWindow.IsShown)
        {
            _dockWindow.HideDock();
        }
        else
        {
            _dockWindow.Configure(_settings.DockPosition, _settings.DockAutoHide, WorkModeService.ShowMedia(_settings.WorkMode));
            _dockWindow.ShowDock();
        }
    }

    private void RefreshDockConnections()
    {
        if (_dockWindow is null) return;
        var connections = _connections.Connections
            .OrderByDescending(c => c.CreatedAt)
            .ToList();
        _dockWindow.SetConnections(connections, Connect, ShowConnectionPassword);
    }

    private void ShowConnectionPassword(ConnectionItem item)
    {
        var password = SecretService.Unprotect(item.EncryptedPassword);
        var win = new ConnectionPasswordWindow(item.Name, password) { Owner = this };
        WindowPlacementService.PlaceOnPrimary(win);
        win.Show();
    }

    private bool UsesTelegramDesktopExport() =>
        !_settings.TelegramSource.Equals("BotApi", StringComparison.OrdinalIgnoreCase);

    private bool CanPollTelegram()
    {
        if (!_settings.TelegramEnabled) return false;
        return UsesTelegramDesktopExport()
            ? !string.IsNullOrWhiteSpace(_settings.TelegramDesktopExportPath)
            : !string.IsNullOrWhiteSpace(SecretService.Unprotect(_settings.TelegramBotTokenEncrypted));
    }

    private async Task PollTelegramTasksAsync(bool force = false, bool showSummary = false)
    {
        if (!force && !_settings.TelegramEnabled) return;

        TelegramPollResult result;
        if (UsesTelegramDesktopExport())
        {
            var path = _settings.TelegramDesktopExportPath;
            if (!force && File.Exists(path))
            {
                var writeUtc = File.GetLastWriteTimeUtc(path);
                if (writeUtc == _lastTelegramDesktopWriteUtc) return;
            }

            result = await _telegramTaskService.ImportDesktopExportAsync(path);
            if (result.Success && File.Exists(path))
            {
                _lastTelegramDesktopWriteUtc = File.GetLastWriteTimeUtc(path);
            }
        }
        else
        {
            result = await _telegramTaskService.PollAsync(_settings);
        }

        if (!result.Success)
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                AddLog("ERR", $"Telegram: {result.Error}");
                if (showSummary) WpfMessageBox.Show(this, result.Error, "Импорт Telegram");
            }
            return;
        }

        if (!UsesTelegramDesktopExport() && result.LastUpdateId > _settings.TelegramLastUpdateId)
        {
            _settings.TelegramLastUpdateId = result.LastUpdateId;
            _settingsStore.Save(_settings);
        }

        var added = ImportTelegramTasks(result.Tasks);
        if (showSummary)
        {
            var source = UsesTelegramDesktopExport() ? "экспорте Telegram Desktop" : "Bot API";
            WpfMessageBox.Show(this,
                $"Проверка завершена.\n\nНайдено задач в {source}: {result.Tasks.Count}\nДобавлено новых: {added}",
                "Импорт Telegram");
        }
    }

    private int ImportTelegramTasks(IEnumerable<TelegramIncomingTask> incomingTasks)
    {
        var added = 0;
        foreach (var incoming in incomingTasks)
        {
            if (_tasks.Tasks.Any(t =>
                    (!string.IsNullOrWhiteSpace(t.TelegramKey) && t.TelegramKey == incoming.TelegramKey) ||
                    (!string.IsNullOrWhiteSpace(t.TelegramExternalId) && t.TelegramExternalId == incoming.Parsed.ExternalId)))
            {
                continue;
            }

            var parsed = incoming.Parsed;
            var startAt = parsed.StartAt.Year >= 2000 ? parsed.StartAt : DateTime.Now;
            var endAt = startAt.AddHours(2);

            var task = new TaskItem
            {
                Title = parsed.Title,
                Description = parsed.Description,
                Status = "Выполняется",
                StartAt = startAt,
                EndAt = endAt,
                ReminderAt = null,
                TelegramKey = incoming.TelegramKey,
                TelegramExternalId = parsed.ExternalId,
                CreatedAt = DateTime.Now
            };
            _tasks.Tasks.Add(task);
            added++;
        }

        if (added <= 0) return 0;

        _tasksStore.Save(_tasks);
        AddLog("OK", $"Telegram: добавлено задач {added}.");
        if (_currentViewKey == "tasks") ShowTasks();
        return added;
    }

    private void RestartTelegramPolling()
    {
        _telegramTimer.Stop();
        _lastTelegramDesktopWriteUtc = DateTime.MinValue;
        if (CanPollTelegram())
        {
            _telegramTimer.Start();
            _ = PollTelegramTasksAsync();
        }
    }




    private void LoadData()
    {
        _projects = _projectStore.Load();
        _notes = _notesStore.Load();
        _connections = _connectionsStore.Load();
        _settings = _settingsStore.Load();
        _tasks = _tasksStore.Load();
        _activity = _activityStore.Load();
        _templates = _templatesStore.Load();
        EnsureProjectTemplateDefaults();
        MigrateCreatedAtDefaults();
        _selectedProject = _projects.Projects.FirstOrDefault();
    }

    private Style RequireStyle(string key)
    {
        if (TryFindResource(key) is Style style) return style;
        if (System.Windows.Application.Current.TryFindResource(key) is Style appStyle) return appStyle;
        throw new InvalidOperationException($"Стиль '{key}' не найден в ресурсах приложения.");
    }

    private void MigrateCreatedAtDefaults()
    {
        var migrated = false;
        foreach (var item in _projects.Projects.Where(p => p.CreatedAt.Year < 2000))
        {
            item.CreatedAt = DateTime.Now;
            migrated = true;
        }
        foreach (var item in _notes.Notes.Where(n => n.CreatedAt.Year < 2000))
        {
            item.CreatedAt = DateTime.Now;
            migrated = true;
        }
        foreach (var item in _connections.Connections.Where(c => c.CreatedAt.Year < 2000))
        {
            item.CreatedAt = DateTime.Now;
            migrated = true;
        }
        foreach (var item in _tasks.Tasks.Where(t => t.CreatedAt.Year < 2000))
        {
            item.CreatedAt = DateTime.Now;
            migrated = true;
        }
        if (migrated)
        {
            _projectStore.Save(_projects);
            _notesStore.Save(_notes);
            _connectionsStore.Save(_connections);
            _tasksStore.Save(_tasks);
        }
    }

    private static string NavIconKey(string key) => key switch
    {
        "projects" => "nav-projects",
        "tasks" => "nav-tasks",
        "notes" => "nav-notes",
        "connections" => "nav-connections",
        "messengers" => "nav-messengers",
        "music" => "music",
        "tiktok" => "nav-tiktok",
        "manga" => "nav-manga",
        "settings" => "nav-settings",
        _ => "nav-projects"
    };

    private void AddNav(string text, string key, Action action, System.Windows.Controls.Panel? target = null)
    {
        _navActions.Add(action);
        var hotkey = _navActions.Count <= 9 ? $" (Ctrl+{_navActions.Count})" : "";
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        _navLabels[key] = label;
        var stack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        stack.Children.Add(MakeIcon(NavIconKey(key), 18));
        stack.Children.Add(label);
        var button = new WpfButton
        {
            Content = stack,
            Tag = key,
            ToolTip = text + hotkey,
            Style = RequireStyle("NavButton")
        };
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) =>
        {
            action();
        };
        _sideNavButtons[key] = button;
        (target ?? NavPanel).Children.Add(button);
    }

    private void UpdateNavHighlight(string? viewKey = null)
    {
        var key = viewKey ?? _currentViewKey ?? "projects";
        string? sideKey = key switch
        {
            "projects"    => "projects",
            "notes"       => "notes",
            "connections" => "connections",
            "tasks"       => "tasks",
            "messengers"  => "messengers",
            "music"       => "music",
            "tiktok"      => "tiktok",
            "manga"       => "manga",
            "settings"    => "settings",
            _ => key.StartsWith("project:", StringComparison.OrdinalIgnoreCase) ? "projects" : null
        };
        if (sideKey is not null)
        {
            _activeSideNavKey = sideKey;
        }

        var activeStyle = _compactSidebarApplied == true ? "NavButtonActiveCompact" : "NavButtonActive";
        foreach (var (navKey, button) in _sideNavButtons)
        {
            button.Style = RequireStyle(navKey.Equals(_activeSideNavKey, StringComparison.OrdinalIgnoreCase)
                ? activeStyle
                : "NavButton");
        }

    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            RestoreFromMaximizedForDrag(e.GetPosition(this));
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove может бросить, если кнопка мыши уже отпущена.
        }
    }

    /// <summary>Как в Windows: при перетаскивании развёрнутого окна — свернуть и держать курсор на title bar.</summary>
    private void RestoreFromMaximizedForDrag(System.Windows.Point mouseInWindow)
    {
        // Общая реализация учитывает DPI и монитор под курсором (см. EditorWindowHelper).
        EditorWindowHelper.RestoreFromMaximizedForDrag(this, mouseInWindow);
        SyncMaximizeButtonIcon();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void GlobalSearch_Click(object sender, RoutedEventArgs e) => ShowGlobalSearch();

    private void DockToggle_Click(object sender, RoutedEventArgs e) => ToggleDock();

    private void Journal_Click(object sender, RoutedEventArgs e)
    {
        _logSessionCount = 0;
        _logSessionErrorCount = 0;
        UpdateLogPreview();
        ShowLogView();
    }

    private void ActiveTaskPill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Не начинать DragMove окна при клике по pill задачи.
        e.Handled = true;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => HideToTray();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);
        // Разворачивание строго в рабочую область монитора (панель задач, разные DPI и размеры мониторов).
        WindowBoundsService.Attach(this);
        // Положение с прошлого запуска (монитор, размер); ниже EnsureVisible зажимает его в видимый экран.
        WindowBoundsService.RestorePlacement(this, _settings.MainWindowPlacement);
        // 1360×860 DIP при масштабе 150% на ноутбуке 1920×1080 больше экрана — ужимаем до рабочей области.
        WindowBoundsService.EnsureVisible(this);
        var handle = new WindowInteropHelper(this).Handle;
        RegisterHotKey(handle, HotkeyNewNote, ModAlt, VkF1);
        RegisterHotKey(handle, HotkeyNewTask, ModAlt, VkF2);
        RegisterHotKey(handle, HotkeyDock, ModAlt | ModControl, VkSpace);
        RegisterHotKey(handle, HotkeySearch, ModControl, VkK);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit)
        {
            SingleInstanceService.Release();
        }

        if (!_allowExit)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, HotkeyNewNote);
        UnregisterHotKey(handle, HotkeyNewTask);
        UnregisterHotKey(handle, HotkeyDock);
        UnregisterHotKey(handle, HotkeySearch);
        _hwndSource?.RemoveHook(WndProc);
        SaveWindowPlacement();
        FlushPendingSaves();
        _dockWindow?.Close();
        _trayIcon?.Dispose();
        DisposeWebApps();
        base.OnClosing(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            var id = wParam.ToInt32();
            if (id == HotkeyNewNote)
            {
                ShowFromTray();
                AddNote(true);
                handled = true;
            }
            else if (id == HotkeyNewTask)
            {
                ShowFromTray();
                AddTask(true);
                handled = true;
            }
            else if (id == HotkeyDock)
            {
                ToggleDock();
                handled = true;
            }
            else if (id == HotkeySearch)
            {
                ShowFromTray();
                ShowGlobalSearch();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void OnWindowVisibilityChanged()
    {
        UpdateGlowAnimationState();
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            _ = UpdateNowPlaying();
            UpdateActiveTaskPill();
        }
    }

    private void ScheduleActivitySave()
    {
        if (!_activitySaveTimer.IsEnabled)
        {
            _activitySaveTimer.Start();
        }
    }

    private void FlushActivityLog()
    {
        _activitySaveTimer.Stop();
        try
        {
            _activityStore.Save(_activity);
        }
        catch
        {
            // Журнал действий не критичен: следующая запись повторит сохранение.
        }
    }

    private void FlushPendingSaves()
    {
        if (_activitySaveTimer.IsEnabled)
        {
            FlushActivityLog();
        }

        FlushMangaLastUrl();
    }

    private void SetupTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть WideS", null, (_, _) => ShowFromTray());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            _allowExit = true;
            _trayIcon?.Dispose();
            Close();
        });

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "WideS",
            Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? ""),
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void InitializeWindowIcons()
    {
        // Все иконки заголовка — единого размера 16 px.
        MinimizeButton.Content = MakeIcon("minus", 16);
        MaximizeButton.Content = MakeIcon("maximize", 16);
        CloseButton.Content = MakeIcon("close", 16);
        BackButton.Content = MakeIcon("back", 16);
        ForwardButton.Content = MakeIcon("forward", 16);
        GlobalSearchButton.Content = MakeIcon("search", 16);
        DockToggleButton.Content = MakeIcon("dock", 16);
        JournalButton.Content = MakeIcon("journal", 16);
        WebZoomOutButton.Content = MakeIcon("minus", 16);
        WebZoomInButton.Content = MakeIcon("plus", 16);
        WebHomeButton.Content = MakeIcon("home", 16);
        WebReloadButton.Content = MakeIcon("reload", 16);
        WebReaderButton.Content = MakeIcon("fullscreen", 16);
        StyleQuietIconButton(BackButton);
        StyleQuietIconButton(ForwardButton);
        StateChanged += (_, _) => ApplyWindowStateChrome();
        ApplyWindowStateChrome();
    }

    private void StyleQuietIconButton(WpfButton button)
    {
        button.Padding = new Thickness(0);
    }

    private void HideToTray()
    {
        SaveWindowPlacement();
        Hide();
    }

    private void SaveWindowPlacement()
    {
        var placement = WindowBoundsService.CapturePlacement(this);
        if (string.IsNullOrEmpty(placement) || placement == _settings.MainWindowPlacement)
        {
            return;
        }

        _settings.MainWindowPlacement = placement;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch
        {
            // Положение окна не критично.
        }
    }

    private void ShowFromTray()
    {
        SingleInstanceService.ActivateWindow(this);
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        ApplyWindowStateChrome();
    }

    private void SyncMaximizeButtonIcon()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = MakeIcon(maximized ? "restore" : "maximize", 16);
        MaximizeButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Восстановить" : "Развернуть");
    }

    private void ApplyWindowStateChrome()
    {
        SyncMaximizeButtonIcon();
        var maximized = WindowState == WindowState.Maximized;
        var radius = maximized ? new CornerRadius(0) : new CornerRadius(8);
        var titleRadius = maximized ? new CornerRadius(0) : new CornerRadius(7, 7, 0, 0);
        RootChromeBorder.CornerRadius = radius;
        TitleBarBorder.CornerRadius = titleRadius;
        if (WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            chrome.CornerRadius = radius;
        }
    }

    private void EnterView(string key)
    {
        if (_immersiveMode && key != "manga")
        {
            SetImmersiveMode(false);
        }

        if (_currentViewKey == key)
        {
            return;
        }

        if (!_isHistoryNavigation && _currentViewKey is not null)
        {
            _backStack.Push(_currentViewKey);
            _forwardStack.Clear();
        }

        _currentViewKey = key;
        UpdateNavHighlight(key);
        UpdateNavigationButtons();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentWebNavigation?.CanGoBack == true)
        {
            CurrentWebNavigation.GoBack();
            return;
        }

        if (_backStack.Count == 0 || _currentViewKey is null) return;
        _forwardStack.Push(_currentViewKey);
        ActivateView(_backStack.Pop(), true);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentWebNavigation?.CanGoForward == true)
        {
            CurrentWebNavigation.GoForward();
            return;
        }

        if (_forwardStack.Count == 0 || _currentViewKey is null) return;
        _backStack.Push(_currentViewKey);
        ActivateView(_forwardStack.Pop(), true);
    }

    private void ActivateView(string key, bool historyNavigation = false)
    {
        _isHistoryNavigation = historyNavigation;
        try
        {
            if (key.StartsWith("project:", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(key["project:".Length..], out var projectId))
            {
                var project = _projects.Projects.FirstOrDefault(p => p.Id == projectId);
                if (project is not null)
                {
                    ShowProjectDetail(project);
                    return;
                }
            }

            switch (key)
            {
                case "notes": ShowNotes(); break;
                case "connections": ShowConnections(); break;
                case "projects": ShowProjects(); break;
                case "tasks": ShowTasks(); break;
                case "messengers": ShowMessengers(); break;
                case "music": ShowMusic(); break;
                case "tiktok": ShowTikTok(); break;
                case "manga": ShowManga(); break;
                case "history": ShowHistory(); break;
                case "settings": ShowSettings(); break;
                default: ShowProjects(); break;
            }
        }
        finally
        {
            _isHistoryNavigation = false;
        }
    }

    private string TabTitle(string key)
    {
        if (key.StartsWith("project:", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(key["project:".Length..], out var projectId))
        {
            return _projects.Projects.FirstOrDefault(p => p.Id == projectId)?.Name ?? "Проект";
        }

        return key switch
        {
            "notes" => "Заметки",
            "connections" => "Подключения",
            "projects" => "Проекты",
            "tasks" => "Задачи",
            "history" => "История",
            "manga" => "Манга",
            "settings" => "Настройки",
            _ => key
        };
    }

















    private static bool ProjectHasWorkspace(ProjectProfile project)
    {
        if (!string.IsNullOrWhiteSpace(project.EditorPath) && File.Exists(project.EditorPath))
        {
            return true;
        }

        return FindWorkspaceFile(project) is not null;
    }















    private void EnsureProjectTemplateDefaults()
    {
        if (_templates.Templates.Count > 0) return;

        _templates.Templates.Add(new ProjectTemplateItem
        {
            Name = "Стандартный проект WideS",
            Folders = ["Docs", "Source", "Tests", "Releases", "_Inbox"],
            NoteTitles = ["README проекта", "Контекст проекта", "Решения и договоренности"]
        });
        _templatesStore.Save(_templates);
    }










 

 




 







    private void AddNote_Click(object sender, RoutedEventArgs e) => AddNote();
    private void AddConnection_Click(object sender, RoutedEventArgs e) => AddConnection();
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenSelectedFolder();































    private void OpenWorkspace()
    {
        var project = RequireProject();
        if (project is null) return;
        OpenWorkspace(project);
    }



    private void ShowGlobalSearch()
    {
        if (_globalSearchWindow is { IsVisible: true })
        {
            _globalSearchWindow.Activate();
            return;
        }

        _globalSearchWindow = new GlobalSearchWindow(SearchAll) { Owner = this };
        _globalSearchWindow.Closed += (_, _) => _globalSearchWindow = null;
        WindowPlacementService.PlaceOnPrimary(_globalSearchWindow);
        _globalSearchWindow.Show();
    }

    private IReadOnlyList<GlobalSearchWindow.SearchHit> SearchAll(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var hits = new List<(DateTime SortAt, GlobalSearchWindow.SearchHit Hit)>();

        foreach (var project in _projects.Projects.Where(p =>
                     p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     p.ProjectFolder.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            hits.Add((project.LastOpenedAt ?? project.CreatedAt,
                new GlobalSearchWindow.SearchHit("Проект", project.Name, project.ProjectFolder, () => ShowProjectDetail(project))));
        }

        foreach (var note in _notes.Notes.Where(n =>
                     n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     n.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            hits.Add((note.UpdatedAt,
                new GlobalSearchWindow.SearchHit("Заметка", note.Title, Preview(note.Text, 80), () => ViewNote(note))));
        }

        foreach (var task in _tasks.Tasks.Where(t =>
                     t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     t.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            hits.Add((task.StartAt,
                new GlobalSearchWindow.SearchHit("Задача", task.Title, TaskStatusText(task), () => EditTask(task))));
        }

        foreach (var connection in _connections.Connections.Where(c =>
                     c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     c.Address.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            hits.Add((connection.CreatedAt,
                new GlobalSearchWindow.SearchHit("Подключение", connection.Name, $"{connection.Type}: {connection.Address}", () => Connect(connection))));
        }

        return hits.OrderByDescending(x => x.SortAt).Select(x => x.Hit).Take(20).ToList();
    }






    private ProjectProfile? RequireProject()
    {
        _selectedProject ??= _projects.Projects.FirstOrDefault();
        if (_selectedProject is not null) return _selectedProject;
        WpfMessageBox.Show(this, "Сначала добавьте проект.", "WideS");
        return null;
    }

    private string PromptText(string title, string placeholder)
    {
        var box = new WpfTextBox { MinWidth = 320, Margin = new Thickness(0, 8, 0, 14) };
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (WpfBrush)FindResource("AppBgBrush"),
            Foreground = (WpfBrush)FindResource("TextBrush")
        };
        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(Text(placeholder, 14, (WpfBrush)FindResource("MutedBrush"), new Thickness()));
        stack.Children.Add(box);
        var buttons = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(ActionButton("OK", () => { dialog.Tag = box.Text.Trim(); dialog.Close(); }));
        buttons.Children.Add(ActionButton("Отмена", () => dialog.Close(), false));
        stack.Children.Add(buttons);
        dialog.Content = stack;
        dialog.Loaded += (_, _) => box.Focus();
        dialog.ShowDialog();
        return dialog.Tag as string ?? "";
    }

    private void Copy(string value, string message)
    {
        WpfClipboard.SetText(value ?? "");
        AddLog("OK", message);
    }

    private void AddLog(string status, string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {status}  {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
        _logSessionCount++;
        if (string.Equals(status, "ERR", StringComparison.OrdinalIgnoreCase))
        {
            _logSessionErrorCount++;
        }
        UpdateLogPreview();
        _activity.Entries.Add(new ActivityEntry
        {
            At = DateTime.Now,
            Status = status,
            Message = message,
            WorkspaceId = _selectedProject?.Id
        });
        if (_activity.Entries.Count > 1000)
        {
            _activity.Entries = _activity.Entries.OrderByDescending(x => x.At).Take(1000).OrderBy(x => x.At).ToList();
        }
        ScheduleActivitySave();
    }

    private WpfBrush ThemeBorderMain() => (WpfBrush)FindResource("BorderMainBrush");

    private void RefreshCurrentView()
    {
        if (!string.IsNullOrWhiteSpace(_currentViewKey))
        {
            ActivateView(_currentViewKey, true);
            return;
        }

        ShowProjects();
    }

    private Border Card(string title)
    {
        var card = new Border { Style = (Style)FindResource("Card"), Width = 352, MinHeight = 158 };
        card.MouseEnter += (_, _) =>
        {
            card.Background = (WpfBrush)FindResource("CardHoverBrush");
            card.BorderBrush = (WpfBrush)FindResource("AccentBorderBrush");
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = (WpfBrush)FindResource("CardBrush");
            card.BorderBrush = (WpfBrush)FindResource("BorderMainBrush");
        };
        return card;
    }

    private Border CardText(string title, string text)
    {
        var card = Card(title);
        card.Child = WithTitle(title, Text(text, 14, (WpfBrush)FindResource("MutedBrush"), new Thickness()));
        return card;
    }

    private Border CardText(string title, string text, Action action)
    {
        var card = CardText(title, text);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.ToolTip = "Нажмите, чтобы открыть";
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                action();
            }
        };
        return card;
    }

    private DockPanel SectionWithActions(Action<WrapPanel> buildActions, out System.Windows.Controls.Panel contentPanel)
    {
        var root = new DockPanel();
        var actions = new WrapPanel { Margin = new Thickness(0) };
        buildActions(actions);

        var actionShell = new Border { Style = (Style)FindResource("SectionToolbar"), Child = actions };
        DockPanel.SetDock(actionShell, Dock.Top);
        root.Children.Add(actionShell);

        contentPanel = ItemsPanel();
        root.Children.Add(contentPanel);
        return root;
    }

    private System.Windows.Controls.Panel ItemsPanel()
    {
        return IsTileView(_viewScope)
            ? new WrapPanel { Margin = new Thickness(0) }
            : new StackPanel { Margin = new Thickness(0) };
    }

    private ViewDisplayMode GetViewMode(string scope) =>
        _viewModes.TryGetValue(scope, out var mode) ? mode : ViewDisplayMode.Tile;

    private bool IsTileView(string scope) => GetViewMode(scope) == ViewDisplayMode.Tile;

    private bool IsListView(string scope) => GetViewMode(scope) == ViewDisplayMode.List;

    private bool IsTableView(string scope) => GetViewMode(scope) == ViewDisplayMode.Table;

    private void AddViewModeButtons(System.Windows.Controls.Panel target, Action refresh)
    {
        target.Children.Add(ViewModeButton(ViewDisplayMode.Tile, refresh));
        target.Children.Add(ViewModeButton(ViewDisplayMode.List, refresh));
        target.Children.Add(ViewModeButton(ViewDisplayMode.Table, refresh));
    }

    private WpfButton ViewModeButton(ViewDisplayMode mode, Action refresh)
    {
        var scope = _viewScope;
        var selected = GetViewMode(scope) == mode;
        var (icon, tip) = mode switch
        {
            ViewDisplayMode.List => ("list", "Список"),
            ViewDisplayMode.Table => ("table", "Таблица"),
            _ => ("grid", "Плитки")
        };
        var button = IconButton(icon, () =>
        {
            _viewModes[scope] = mode;
            _viewScope = scope;
            refresh();
        }, tip, 36);
        ApplyButtonTone(button, tip, selected);
        if (!selected)
        {
            StyleQuietIconButton(button);
        }

        button.ToolTip = tip;
        return button;
    }

    private WpfButton ViewButton(string text, bool listView, Action refresh)
    {
        var scope = _viewScope;
        var selected = IsListView(scope) == listView;
        var button = IconButton(listView ? "list" : "grid", () =>
        {
            _viewModes[scope] = listView ? ViewDisplayMode.List : ViewDisplayMode.Tile;
            _viewScope = scope;
            refresh();
        }, text, 36);
        ApplyButtonTone(button, text, selected);
        if (!selected)
        {
            StyleQuietIconButton(button);
        }
        button.ToolTip = text;
        return button;
    }

    private bool IsRaycastUi => ThemeService.UsesAmbientGlow(_settings.AccentTheme);

    private WpfButton FilterButton(string text, bool selected, Action action)
    {
        var button = new WpfButton
        {
            Content = text,
            Style = (Style)FindResource("GhostButton"),
            Height = IsRaycastUi ? 32 : 36,
            MinWidth = IsRaycastUi ? 64 : 78,
            MaxHeight = 36,
            Padding = IsRaycastUi ? new Thickness(14, 5, 14, 5) : new Thickness(12, 6, 12, 6),
            Margin = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        void ApplyVisual()
        {
            if (IsRaycastUi)
            {
                // Выбранный фильтр — светлая подложка и красный текст, а не сплошная красная заливка:
                // так он не спорит с главной кнопкой действия.
                button.Background = selected
                    ? (WpfBrush)FindResource("ElevatedBgBrush")
                    : System.Windows.Media.Brushes.Transparent;
                button.BorderBrush = (WpfBrush)FindResource("BorderSubtleBrush");
                button.BorderThickness = new Thickness(0);
                button.Foreground = selected
                    ? (WpfBrush)FindResource("AccentBrush")
                    : (WpfBrush)FindResource("TextBrush");
                button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
            else
            {
                button.Background = selected
                    ? (WpfBrush)FindResource("FilterBgBrush")
                    : (WpfBrush)FindResource("GhostButtonBgBrush");
                button.BorderBrush = selected
                    ? (WpfBrush)FindResource("FilterBorderBrush")
                    : (WpfBrush)FindResource("BorderMainBrush");
                button.Foreground = (WpfBrush)FindResource("TextBrush");
            }
        }

        ApplyVisual();
        button.MouseEnter += (_, _) =>
        {
            if (IsRaycastUi && !selected)
            {
                button.Background = (WpfBrush)FindResource("NavHoverBgBrush");
            }
            else if (!IsRaycastUi)
            {
                button.Background = selected
                    ? (WpfBrush)FindResource("FilterBgBrush")
                    : (WpfBrush)FindResource("CardHoverBrush");
            }
        };
        button.MouseLeave += (_, _) => ApplyVisual();
        button.Click += (_, _) => action();
        return button;
    }

    private Border ToolbarGap(double width = 8)
    {
        return new Border { Width = width, Height = 1, Margin = new Thickness(2, 0, 2, 0) };
    }

    private void ApplyCardView(Border card, double tileWidth = 340)
    {
        var mode = GetViewMode(_viewScope);
        card.Width = mode == ViewDisplayMode.Tile ? tileWidth : double.NaN;
        card.HorizontalAlignment = mode == ViewDisplayMode.Tile
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Stretch;
        card.MinHeight = mode == ViewDisplayMode.Tile ? 200 : 44;
    }

    private Border ListRow(string title, Action action, WpfBrush? marker = null, params UIElement[] actions)
    {
        var row = new Border
        {
            Background = (WpfBrush)FindResource("CardBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 7, 10, 7),
            Margin = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            MinHeight = 40,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var panel = new DockPanel();
        if (actions.Length > 0)
        {
            var actionPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            foreach (var item in actions)
            {
                actionPanel.Children.Add(item);
            }
            DockPanel.SetDock(actionPanel, Dock.Right);
            panel.Children.Add(actionPanel);
        }

        if (marker is not null)
        {
            panel.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = marker,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            });
        }

        panel.Children.Add(Text(title, 15, (WpfBrush)FindResource("TextBrush"), new Thickness(), FontWeights.SemiBold));
        row.Child = panel;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                e.Handled = true;
                action();
            }
        };
        return row;
    }

    private Border ProjectEntityRow(string title, Action open, WpfBrush? marker, Action edit, Action delete)
    {
        var row = ListRow(
            title,
            open,
            marker,
            RowActionButton("edit", "Изменить", edit),
            RowActionButton("delete", "Удалить", delete));
        row.Padding = new Thickness(10, 5, 10, 5);
        row.MinHeight = 34;
        return row;
    }

    private WpfButton RowActionButton(string icon, string tooltip, Action action)
    {
        var button = IconButton(icon, action, tooltip, 26);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new Thickness(2, 0, 0, 0);
        return button;
    }

    private WpfBrush ImportanceBrush(string importance)
    {
        return importance.Equals("Red", StringComparison.OrdinalIgnoreCase)
            ? (WpfBrush)FindResource("DangerBrush")
            : importance.Equals("Yellow", StringComparison.OrdinalIgnoreCase)
                ? (WpfBrush)FindResource("WarnBrush")
                : (WpfBrush)FindResource("SuccessBrush");
    }

    private static string ImportanceText(string importance)
    {
        return importance.Equals("Red", StringComparison.OrdinalIgnoreCase) ? "Срочно"
            : importance.Equals("Yellow", StringComparison.OrdinalIgnoreCase) ? "Средне"
            : "Не срочно";
    }

    private static string TaskStatusText(TaskItem task)
    {
        if (!string.IsNullOrWhiteSpace(task.Status))
        {
            return task.Status;
        }

        return task.IsDone ? "Архив" : "Новая";
    }


    private ProjectProfile? GetCreationContextProject(bool forceCommon)
    {
        if (!forceCommon && _selectedProject is not null &&
            (_viewScope is "project-detail" or "project-notes"))
        {
            return _selectedProject;
        }

        if (_focusProject is not null)
        {
            return _focusProject;
        }

        return null;
    }

    private bool ShouldUseToastReminder() =>
        !IsVisible || WindowState == WindowState.Minimized || !IsActive;

    private void OnTaskNotificationActivated(TaskNotificationAction action)
    {
        Dispatcher.Invoke(() => HandleTaskNotificationAction(action));
    }

    private void HandleTaskNotificationAction(TaskNotificationAction action)
    {
        if (action.Action == "activate")
        {
            ShowFromTray();
            return;
        }

        var task = _tasks.Tasks.FirstOrDefault(t => t.Id == action.TaskId);
        if (task is null)
        {
            ShowFromTray();
            return;
        }

        switch (action.Action)
        {
            case "start":
                StartTask(task, openEditor: false);
                break;
            case "snooze":
                SnoozeTask(task, TimeSpan.FromMinutes(action.SnoozeMinutes));
                break;
            default:
                ShowFromTray();
                EditTask(task);
                break;
        }
    }

    private void SnoozeTask(TaskItem task, TimeSpan snooze)
    {
        var duration = task.EndAt > task.StartAt
            ? task.EndAt - task.StartAt
            : TimeSpan.FromHours(1);
        task.StartAt = DateTime.Now.Add(snooze);
        task.EndAt = task.StartAt.Add(duration);
        task.ReminderAt = task.StartAt;
        task.LastNotifiedAt = null;
        task.Status = $"Отложено до {task.StartAt:dd.MM.yyyy HH:mm}";
        _tasksStore.Save(_tasks);
        AddLog("OK", $"Задача отложена: {task.Title}");
        TaskNotificationService.ClearReminder(task.Id);
        RefreshTasksView();
    }

    private static bool IsTaskRunning(TaskItem task) =>
        task.Status.Equals("Выполняется", StringComparison.OrdinalIgnoreCase);

    private static bool IsTaskPaused(TaskItem task) =>
        task.Status.Equals("На паузе", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldRemindTask(TaskItem task, DateTime now)
    {
        if (task.IsDone || IsTaskRunning(task) || IsTaskPaused(task)) return false;

        var remindAt = task.ReminderAt ?? task.StartAt;
        if (remindAt > now) return false;

        return task.LastNotifiedAt is null || task.LastNotifiedAt.Value.AddMinutes(1) <= now;
    }


    private StackPanel BaseCardStack(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(title, 16, (WpfBrush)FindResource("TextBrush"), new Thickness(0, 0, 0, 10), FontWeights.SemiBold));
        return stack;
    }

    private StackPanel WithTitle(string title, UIElement content)
    {
        var stack = BaseCardStack(title);
        stack.Children.Add(content);
        return stack;
    }

    private WpfButton ActionButton(string text, Action action, bool primary = true)
    {
        var button = new WpfButton
        {
            Content = text,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"),
            Height = 36,
            MinWidth = 78,
            MaxHeight = 36,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(3, 3, 3, 3),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        ApplyButtonTone(button, text, primary);
        button.Click += (_, _) => action();
        return button;
    }

    private void ApplyButtonTone(WpfButton button, string text, bool primary)
    {
        var lower = text.ToLowerInvariant();
        var bgKey = primary ? "PrimaryButtonBgBrush" : "GhostButtonBgBrush";
        var borderKey = primary ? "PrimaryButtonBgBrush" : "GhostButtonBorderBrush";
        var foregroundKey = primary ? "AccentForegroundBrush" : "TextBrush";

        if (lower.Contains("удал") || lower.Contains("очист"))
        {
            bgKey = "DangerSoftBgBrush";
            borderKey = "DangerBrush";
            foregroundKey = "TextBrush";
        }
        else if (lower.Contains("пауза") || lower.Contains("отлож"))
        {
            bgKey = "FocusWarnBgBrush";
            borderKey = "WarnBrush";
            foregroundKey = "TextBrush";
        }
        else if (lower.Contains("заверш"))
        {
            bgKey = "SuccessSoftBgBrush";
            borderKey = "SuccessBrush";
            foregroundKey = "TextBrush";
        }
        else if ((lower.Contains("плит") || lower.Contains("спис")) && primary)
        {
            bgKey = "FilterBgBrush";
            borderKey = "FilterBorderBrush";
            foregroundKey = "TextBrush";
        }
        else if (IsRaycastUi && primary)
        {
            // Стеклянная заливка: без контура, только полупрозрачный цвет.
            bgKey = "PrimaryButtonBgBrush";
            borderKey = "PrimaryButtonBgBrush";
            foregroundKey = "AccentForegroundBrush";
            button.BorderThickness = new Thickness(0);
        }
        else if (IsRaycastUi)
        {
            button.BorderThickness = new Thickness(0);
        }

        button.Background = (WpfBrush)FindResource(bgKey);
        button.BorderBrush = (WpfBrush)FindResource(borderKey);
        button.Foreground = primary && foregroundKey == "AccentForegroundBrush"
            ? WpfBrushes.White
            : (WpfBrush)FindResource(foregroundKey);
    }

    private WpfButton LinkAction(string text, Action action)
    {
        var button = new WpfButton
        {
            Content = text,
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (WpfBrush)FindResource("MutedBrush"),
            Padding = new Thickness(0, 4, 16, 4),
            Margin = new Thickness(0, 2, 10, 2),
            Height = 28,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => action();
        return button;
    }

    private WpfButton EditIconButton(Action action)
    {
        var button = IconButton("edit", action, "Редактировать", 30);
        button.Background = (WpfBrush)FindResource("ElevatedBgBrush");
        button.BorderBrush = (WpfBrush)FindResource("BorderMainBrush");
        button.Foreground = (WpfBrush)FindResource("TextBrush");
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Thickness(0);
        return button;
    }

    private WpfButton IconButton(string iconName, Action action, string tooltip, double size = 32)
    {
        var button = new WpfButton
        {
            Style = (Style)FindResource("GhostButton"),
            Content = MakeIcon(iconName, Math.Max(12, size - 16)),
            Width = size,
            Height = size,
            MinWidth = size,
            MaxHeight = size,
            Padding = new Thickness(0),
            Margin = new Thickness(3, 3, 3, 3),
            Background = (WpfBrush)FindResource("PanelBrush"),
            BorderBrush = (WpfBrush)FindResource("BorderMainBrush"),
            Foreground = (WpfBrush)FindResource("IconBrush"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Top
        };
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private FrameworkElement MakeIcon(string iconName, double size)
    {
        return UiIconFactory.Create(iconName, size);
    }

    private WpfTextBox SearchBox(string hint, Action render) => UiHelpers.CreateSearchBox(hint, render);

    private TextBlock Muted(string text) => Text(text, 13, (WpfBrush)FindResource("MutedBrush"), new Thickness());

    private TextBlock Text(string text, double size, WpfBrush brush, Thickness margin, FontWeight? weight = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = brush,
            Margin = margin,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = weight ?? FontWeights.Normal
        };
    }

    private static string Preview(string text, int length)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(пусто)";
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= length ? text : text[..length] + "...";
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is WpfButton)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static string? FindWorkspaceFile(ProjectProfile project)
    {
        if (!string.IsNullOrWhiteSpace(project.WorkspacePath) && File.Exists(project.WorkspacePath))
        {
            return project.WorkspacePath;
        }

        if (!Directory.Exists(project.ProjectFolder))
        {
            return null;
        }

        var direct = Directory.EnumerateFiles(project.ProjectFolder, "*.code-workspace", SearchOption.TopDirectoryOnly)
            .OrderBy(File.GetLastWriteTime)
            .LastOrDefault();
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        return Directory.EnumerateFiles(project.ProjectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsLikelyWorkspaceFile)
            .OrderBy(File.GetLastWriteTime)
            .LastOrDefault();
    }

    private static bool IsLikelyWorkspaceFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 128 * 1024) return false;
            var text = File.ReadAllText(path);
            return text.Contains("\"folders\"", StringComparison.OrdinalIgnoreCase) &&
                   text.Contains("\"settings\"", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

}

