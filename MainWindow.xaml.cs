using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private const int WmClipboardUpdate = 0x031D;
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;
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
    private readonly JsonFileStore<CommandRecipesStoreData> _commandRecipesStore = new(AppPaths.CommandRecipesJson);
    private readonly JsonFileStore<AiAgentsStoreData> _aiAgentsStore = new(AppPaths.AiAgentsJson);
    private readonly JsonFileStore<TasksStoreData> _tasksStore = new(AppPaths.TasksJson);
    private readonly JsonFileStore<ActivityStoreData> _activityStore = new(AppPaths.ActivityJson);
    private readonly JsonFileStore<ProjectTemplatesStoreData> _templatesStore = new(AppPaths.ProjectTemplatesJson);

    private ProjectStoreData _projects = new();
    private NotesStoreData _notes = new();
    private ConnectionsStoreData _connections = new();
    private AppSettingsData _settings = new();
    private CommandRecipesStoreData _commandRecipes = new();
    private AiAgentsStoreData _aiAgents = new();
    private TasksStoreData _tasks = new();
    private ActivityStoreData _activity = new();
    private ProjectTemplatesStoreData _templates = new();
    private ProjectProfile? _selectedProject;
    private ProjectProfile? _focusProject;
    private DateTime? _focusStartedAt;
    private readonly List<string> _droppedFiles = [];
    private readonly Dictionary<string, ViewDisplayMode> _viewModes = new();
    private string _viewScope = "home";
    private string _projectDetailTab = "notes";
    private bool _projectTasksArchive;
    private string _browserCategoryFilter = "AI Agents";
    private readonly Dictionary<string, WpfButton> _sideNavButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WpfButton> _topNavButtons = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeSideNavKey = "home";
    private string? _activeTopNavKey;
    private string _connectionTypeFilter = "Все";
    private bool _showTaskArchive;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private readonly List<string> _openTabs = [];
    private string? _currentViewKey;
    private bool _isHistoryNavigation;
    private readonly DispatcherTimer _taskTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _pillTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly MediaService _mediaService = new();
    private readonly DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _telegramTimer = new() { Interval = TimeSpan.FromSeconds(45) };
    private readonly TelegramTaskService _telegramTaskService = new();
    private TaskReminderWindow? _activeReminderWindow;
    private Guid? _activeTaskPillId;
    private Guid? _trackedTaskId;
    private Forms.NotifyIcon? _trayIcon;
    private bool _allowExit;
    private HwndSource? _hwndSource;
    private FloatingDockWindow? _dockWindow;
    private string? _lastClipboardImageHash;
    private DateTime _lastScreenshotPromptAt = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        AppPaths.EnsureDataDirectory();
        InitializeWindowIcons();
        LoadData();
        AfterLoadData();
        BuildNavGrouped();
        SetupTrayIcon();
        _taskTimer.Tick += (_, _) => CheckTaskReminders();
        _taskTimer.Start();
        _pillTimer.Tick += (_, _) => UpdateActiveTaskPill();
        Loaded += (_, _) =>
        {
            SingleInstanceService.StartActivationListener(this, _ => ShowFromTray());
            CheckTaskReminders();
            UpdateActiveTaskPill();
        };
        TaskNotificationService.NotificationActivated += OnTaskNotificationActivated;
        ShowHome();
        InitializeNowPlaying();
        InitializeTelegramPolling();
        InitializePortal();
        AddLog("OK", "WideS запущен.");
    }

    private void InitializeTelegramPolling()
    {
        _telegramTimer.Tick += async (_, _) => await PollTelegramTasksAsync();
        if (_settings.TelegramEnabled && !string.IsNullOrWhiteSpace(SecretService.Unprotect(_settings.TelegramBotTokenEncrypted)))
        {
            _telegramTimer.Start();
            _ = PollTelegramTasksAsync();
        }
    }

    private async void InitializeNowPlaying()
    {
        NpPrev.Content = MakeIcon("prev", 14);
        NpPlay.Content = MakeIcon("play", 15);
        NpNext.Content = MakeIcon("next", 14);
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
            return;
        }

        var snap = await _mediaService.GetSnapshotAsync();
        if (!snap.HasSession)
        {
            NowPlayingPanel.Visibility = Visibility.Collapsed;
            return;
        }

        NowPlayingPanel.Visibility = Visibility.Visible;
        NpTitle.Text = snap.Title;
        NpArtist.Text = snap.Artist;
        NpSource.Text = string.IsNullOrWhiteSpace(snap.Source) ? "СЕЙЧАС ИГРАЕТ" : snap.Source.ToUpperInvariant();
        NpPlay.Content = MakeIcon(snap.IsPlaying ? "pause" : "play", 15);
        UiHelpers.SetAlbumArt(NpAlbumArt, snap.AlbumArt);
        if (_settings.CompactSidebar)
        {
            NpAlbumArt.Visibility = Visibility.Collapsed;
        }
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
            () => { ShowFromTray(); ShowDropZone(); },
            ShowFromTray,
            ImportFilesToDropZone);

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
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.CreatedAt)
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

    private async Task PollTelegramTasksAsync()
    {
        if (!_settings.TelegramEnabled) return;

        var result = await _telegramTaskService.PollAsync(_settings);
        if (!result.Success)
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                AddLog("ERR", $"Telegram: {result.Error}");
            }
            return;
        }

        if (result.LastUpdateId > _settings.TelegramLastUpdateId)
        {
            _settings.TelegramLastUpdateId = result.LastUpdateId;
            _settingsStore.Save(_settings);
        }

        var added = 0;
        foreach (var incoming in result.Tasks)
        {
            if (_tasks.Tasks.Any(t =>
                    (!string.IsNullOrWhiteSpace(t.TelegramKey) && t.TelegramKey == incoming.TelegramKey) ||
                    (!string.IsNullOrWhiteSpace(t.TelegramExternalId) && t.TelegramExternalId == incoming.Parsed.ExternalId)))
            {
                continue;
            }

            var parsed = incoming.Parsed;
            var endAt = parsed.StartAt.AddHours(2);
            if (endAt <= parsed.StartAt)
            {
                endAt = parsed.StartAt.AddHours(1);
            }

            var task = new TaskItem
            {
                Title = parsed.Title,
                Description = parsed.Description,
                Status = "Выполняется",
                StartAt = parsed.StartAt,
                EndAt = endAt,
                ReminderAt = null,
                ContactName = parsed.ContactName,
                ContactPhone = parsed.ContactPhone,
                TelegramKey = incoming.TelegramKey,
                TelegramExternalId = parsed.ExternalId,
                CreatedAt = DateTime.Now
            };
            _tasks.Tasks.Add(task);
            added++;
        }

        if (added <= 0) return;

        _tasksStore.Save(_tasks);
        AddLog("OK", $"Telegram: добавлено задач {added}.");
        if (_currentViewKey == "tasks") ShowTasks();
        else if (_currentViewKey == "home") ShowHome();
    }

    private void RestartTelegramPolling()
    {
        _telegramTimer.Stop();
        if (_settings.TelegramEnabled && !string.IsNullOrWhiteSpace(SecretService.Unprotect(_settings.TelegramBotTokenEncrypted)))
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
        _commandRecipes = _commandRecipesStore.Load();
        _aiAgents = _aiAgentsStore.Load();
        _tasks = _tasksStore.Load();
        _activity = _activityStore.Load();
        _templates = _templatesStore.Load();
        EnsureCommandRecipeDefaults();
        EnsureAiAgentDefaults();
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
        foreach (var item in _aiAgents.Agents.Where(a => a.CreatedAt.Year < 2000))
        {
            item.CreatedAt = DateTime.Now;
            migrated = true;
        }
        foreach (var item in _commandRecipes.Recipes.Where(r => r.CreatedAt.Year < 2000))
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
            _aiAgentsStore.Save(_aiAgents);
            _commandRecipesStore.Save(_commandRecipes);
        }
    }

    private static string NavIconKey(string key) => key switch
    {
        "home" => "nav-home",
        "projects" => "nav-projects",
        "tasks" => "nav-tasks",
        "notes" => "nav-notes",
        "connections" => "nav-connections",
        "contacts" => "nav-contacts",
        "ai" => "nav-browser",
        "commands" => "nav-commands",
        "backup" => "nav-backup",
        "dropzone" => "nav-dropzone",
        "settings" => "nav-settings",
        _ => "nav-home"
    };

    private void AddNav(string text, string key, Action action)
    {
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
            ToolTip = text,
            Style = RequireStyle("NavButton")
        };
        button.Click += (_, _) =>
        {
            _activeTopNavKey = null;
            action();
        };
        _sideNavButtons[key] = button;
        NavPanel.Children.Add(button);
    }

    private void AddTopNav(string text, string key, Action action)
    {
        var button = new WpfButton
        {
            Content = text,
            Tag = key,
            Style = RequireStyle("TopNavPill")
        };
        button.Click += (_, _) => action();
        _topNavButtons[key] = button;
        TopNavPanel.Children.Add(button);
    }

    private void UpdateNavHighlight(string? viewKey = null)
    {
        var key = viewKey ?? _currentViewKey ?? "home";
        string? sideKey = key switch
        {
            "home"        => "home",
            "projects"    => "projects",
            "tasks"       => "tasks",
            "notes"       => "notes",
            "connections" => "connections",
            "contacts"    => "contacts",
            "ai"          => "ai",
            "commands"    => "commands",
            "backup"      => "backup",
            "dropzone"    => "dropzone",
            "settings"    => "settings",
            _ => key.StartsWith("project:", StringComparison.OrdinalIgnoreCase) ? "projects" : null
        };
        var topKey = key is "favorites" or "history" ? key : null;

        if (sideKey is not null)
        {
            _activeSideNavKey = sideKey;
            _activeTopNavKey = null;
        }
        else if (topKey is not null)
        {
            _activeTopNavKey = topKey;
        }

        foreach (var (navKey, button) in _sideNavButtons)
        {
            button.Style = RequireStyle(navKey.Equals(_activeSideNavKey, StringComparison.OrdinalIgnoreCase)
                ? "NavButtonActive"
                : "NavButton");
        }

        foreach (var (navKey, button) in _topNavButtons)
        {
            button.Style = RequireStyle(navKey.Equals(_activeTopNavKey, StringComparison.OrdinalIgnoreCase)
                ? "TopNavPillActive"
                : "TopNavPill");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => HideToTray();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);
        var handle = new WindowInteropHelper(this).Handle;
        RegisterHotKey(handle, HotkeyNewNote, ModAlt, VkF1);
        RegisterHotKey(handle, HotkeyNewTask, ModAlt, VkF2);
        RegisterHotKey(handle, HotkeyDock, ModAlt | ModControl, VkSpace);
        RegisterHotKey(handle, HotkeySearch, ModControl, VkK);
        if (_settings.ClipboardScreenshotPrompt)
        {
            AddClipboardFormatListener(handle);
        }
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
        RemoveClipboardFormatListener(handle);
        _hwndSource?.RemoveHook(WndProc);
        _dockWindow?.Close();
        _trayIcon?.Dispose();
        ShutdownPortalService();
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
        else if (msg == WmClipboardUpdate)
        {
            Dispatcher.BeginInvoke(HandleClipboardUpdate);
        }
        else if (msg == WmGetMinMaxInfo)
        {
            ApplyMaximizeWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ApplyMaximizeWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var work = monitorInfo.rcWork;
        var monitorRect = monitorInfo.rcMonitor;
        mmi.ptMaxPosition.X = Math.Abs(work.Left - monitorRect.Left);
        mmi.ptMaxPosition.Y = Math.Abs(work.Top - monitorRect.Top);
        mmi.ptMaxSize.X = Math.Abs(work.Right - work.Left);
        mmi.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private void UpdateClipboardScreenshotListener()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        RemoveClipboardFormatListener(handle);
        if (_settings.ClipboardScreenshotPrompt)
        {
            AddClipboardFormatListener(handle);
        }
    }

    private void HandleClipboardUpdate()
    {
        if (!_settings.ClipboardScreenshotPrompt) return;
        if (DateTime.Now - _lastScreenshotPromptAt < TimeSpan.FromMilliseconds(700)) return;
        if (!ClipboardHasBitmapWithoutText()) return;

        BitmapSource? image;
        try
        {
            if (!WpfClipboard.ContainsImage()) return;
            image = WpfClipboard.GetImage();
        }
        catch
        {
            return;
        }

        if (image is null) return;
        var hash = HashBitmap(image);
        if (!string.IsNullOrWhiteSpace(hash) && hash == _lastClipboardImageHash) return;
        _lastClipboardImageHash = hash;
        _lastScreenshotPromptAt = DateTime.Now;

        var project = _focusProject ?? _selectedProject ?? _projects.Projects.FirstOrDefault();
        if (project is null) return;

        ShowFromTray();
        var result = WpfMessageBox.Show(this,
            $"Сохранить скриншот в папку дня проекта \"{project.Name}\"?",
            "WideS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var screenshots = Path.Combine(AppPaths.EnsureTodayWorkDay(project), "Screenshots");
            Directory.CreateDirectory(screenshots);
            var path = Path.Combine(screenshots, $"screenshot-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.png");
            SaveBitmap(image, path);
            AddLog("OK", $"Скриншот сохранен: {path}");
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Скриншот: {ex.Message}");
        }
    }

    private static string HashBitmap(BitmapSource image)
    {
        try
        {
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
        }
        catch
        {
            return "";
        }
    }

    private static void SaveBitmap(BitmapSource image, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
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
        MinimizeButton.Content = MakeIcon("minus", 16);
        MaximizeButton.Content = MakeIcon("maximize", 16);
        CloseButton.Content = MakeIcon("close", 16);
        BackButton.Content = MakeIcon("back", 18);
        ForwardButton.Content = MakeIcon("forward", 18);
        StyleQuietIconButton(BackButton);
        StyleQuietIconButton(ForwardButton);
    }

    private void StyleQuietIconButton(WpfButton button)
    {
        button.Background = (WpfBrush)FindResource("PanelBrush");
        button.BorderBrush = ThemeBorderMain();
        button.Foreground = (WpfBrush)FindResource("TextBrush");
        button.Padding = new Thickness(0);
    }

    private void HideToTray()
    {
        Hide();
    }

    private void ShowFromTray()
    {
        SingleInstanceService.ActivateWindow(this);
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = MakeIcon(WindowState == WindowState.Maximized ? "restore" : "maximize", 16);
    }

    private void LogPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => ExpandLogPanel();

    private void LogPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => CollapseLogPanel();

    private void LogPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (LogBox.Visibility == Visibility.Visible)
        {
            ShowLogView();
        }
        else
        {
            ExpandLogPanel();
        }
    }

    private void CollapseLogPanel()
    {
        _logExpanded = false;
        LogPanel.Height = 38;
        LogBox.Visibility = Visibility.Collapsed;
        LogPreview.Visibility = Visibility.Visible;
    }

    private void ExpandLogPanel()
    {
        if (_logExpanded) return;
        _logExpanded = true;
        LogPanel.Height = 150;
        LogBox.Visibility = Visibility.Visible;
        LogPreview.Visibility = Visibility.Collapsed;
        _logSessionCount = 0;
        UpdateLogPreview();
    }

    private void EnterView(string key)
    {
        if (_currentViewKey == key)
        {
            RenderTabs();
            return;
        }

        if (!_isHistoryNavigation && _currentViewKey is not null)
        {
            _backStack.Push(_currentViewKey);
            _forwardStack.Clear();
        }

        _currentViewKey = key;
        if (IsTabView(key) && !_openTabs.Contains(key))
        {
            _openTabs.Add(key);
        }
        RenderTabs();
        UpdateNavHighlight(key);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_backStack.Count == 0 || _currentViewKey is null) return;
        _forwardStack.Push(_currentViewKey);
        ActivateView(_backStack.Pop(), true);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
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
                case "home": ShowHome(); break;
                case "notes": ShowNotes(); break;
                case "connections": ShowConnections(); break;
                case "projects": ShowProjects(); break;
                case "favorites": ShowFavorites(); break;
                case "tasks": ShowTasks(); break;
                case "contacts": ShowContacts(); break;
                case "commands": ShowCommandRecipes(); break;
                case "ai": ShowAiAgents(); break;
                case "history": ShowHistory(); break;
                case "dropzone": ShowDropZone(); break;
                case "backup": ShowBackupContext(); break;
                case "settings": ShowSettings(); break;
                default: ShowHome(); break;
            }
        }
        finally
        {
            _isHistoryNavigation = false;
        }
    }

    private void RenderTabs()
    {
        TabsPanel.Children.Clear();
        foreach (var key in _openTabs.Where(IsTabView).ToList())
        {
            var title = TabTitle(key);
            var shell = new Border
            {
                Height = 30,
                MinWidth = 120,
                Padding = new Thickness(10, 0, 4, 0),
                Margin = new Thickness(0, 2, 6, 2),
                CornerRadius = new CornerRadius(8),
                Background = key == _currentViewKey
                    ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#008B8B"))
                    : (WpfBrush)FindResource("PanelBrush"),
                BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#008B8B")),
                BorderThickness = new Thickness(1),
                ToolTip = title
            };
            var row = new DockPanel { LastChildFill = true };
            var close = IconButton("close", () =>
            {
                _openTabs.Remove(key);
                if (_currentViewKey == key)
                {
                    ShowProjects();
                }
                else
                {
                    RenderTabs();
                }
            }, "Закрыть вкладку", 24);
            close.Width = 24;
            close.Height = 24;
            close.Margin = new Thickness(8, 2, 0, 2);
            DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
            row.Children.Add(close);

            var tab = new WpfButton
            {
                Content = title,
                Height = 28,
                MinWidth = 72,
                MaxHeight = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Background = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (WpfBrush)FindResource("TextBrush"),
                ToolTip = title
            };
            tab.Click += (_, _) => ActivateView(key);
            row.Children.Add(tab);
            shell.Child = row;
            TabsPanel.Children.Add(shell);
        }
    }

    private static bool IsTabView(string key) => key.StartsWith("project:", StringComparison.OrdinalIgnoreCase);

    private string TabTitle(string key)
    {
        if (key.StartsWith("project:", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(key["project:".Length..], out var projectId))
        {
            return _projects.Projects.FirstOrDefault(p => p.Id == projectId)?.Name ?? "Проект";
        }

        return key switch
        {
            "home" => "Главная",
            "notes" => "Заметки",
            "connections" => "Подключения",
            "projects" => "Проекты",
            "favorites" => "Избранное",
            "tasks" => "Задачи",
            "contacts" => "Клиенты",
            "commands" => "Команды",
            "ai" => "Браузер",
            "history" => "История",
            "dropzone" => "DropZone",
            "backup" => "Backup",
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
            NoteTitles = ["README проекта", "Контекст проекта", "Решения и договоренности"],
            TaskTitles = ["Проверить структуру проекта", "Собрать первый backup", "Подготовить context.txt"]
        });
        _templatesStore.Save(_templates);
    }






    private static IEnumerable<string> ExtractVariables(string command)
    {
        return Regex.Matches(command, "\\{([a-zA-Z0-9_а-яА-ЯёЁ-]+)\\}")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string QuotePowerShell(string command)
    {
        return "\"" + command.Replace("\"", "\\\"") + "\"";
    }





    private void ToggleNotePinned(NoteItem note)
    {
        note.IsPinned = !note.IsPinned;
        _notesStore.Save(_notes);
        AddLog("OK", note.IsPinned ? $"Заметка добавлена в избранное: {note.Title}" : $"Заметка убрана из избранного: {note.Title}");
        RefreshAfterPinnedChange();
    }

    private void ToggleConnectionPinned(ConnectionItem connection)
    {
        connection.IsPinned = !connection.IsPinned;
        _connectionsStore.Save(_connections);
        AddLog("OK", connection.IsPinned ? $"Подключение добавлено в избранное: {connection.Name}" : $"Подключение убрано из избранного: {connection.Name}");
        RefreshAfterPinnedChange();
    }

    private void ToggleTaskPinned(TaskItem task)
    {
        task.IsPinned = !task.IsPinned;
        _tasksStore.Save(_tasks);
        AddLog("OK", task.IsPinned ? $"Задача добавлена в избранное: {task.Title}" : $"Задача убрана из избранного: {task.Title}");
        RefreshAfterPinnedChange();
    }

    private void ToggleAiAgentPinned(AiAgentItem agent)
    {
        agent.IsPinned = !agent.IsPinned;
        _aiAgentsStore.Save(_aiAgents);
        AddLog("OK", agent.IsPinned ? $"Ссылка добавлена в избранное: {agent.Name}" : $"Ссылка убрана из избранного: {agent.Name}");
        RefreshAfterPinnedChange();
    }

    private void RefreshAfterPinnedChange()
    {
        if (_viewScope == "favorites")
        {
            ShowFavorites();
            return;
        }

        ActivateView(_currentViewKey ?? _viewScope, true);
    }




    private int PinnedCount()
        => _projects.Projects.Count(p => p.IsPinned)
         + _notes.Notes.Count(n => n.IsPinned)
         + _connections.Connections.Count(c => c.IsPinned)
         + _tasks.Tasks.Count(t => t.IsPinned)
         + _aiAgents.Agents.Count(a => a.IsPinned);

    private string PinnedText()
    {
        var items = new List<string>();
        items.AddRange(_projects.Projects.Where(p => p.IsPinned).Take(5).Select(p => "Проект: " + p.Name));
        items.AddRange(_tasks.Tasks.Where(t => t.IsPinned && !t.IsDone).Take(5).Select(t => "Задача: " + t.Title));
        items.AddRange(_notes.Notes.Where(n => n.IsPinned).Take(5).Select(n => "Заметка: " + n.Title));
        items.AddRange(_connections.Connections.Where(c => c.IsPinned).Take(5).Select(c => "Подключение: " + c.Name));
        items.AddRange(_aiAgents.Agents.Where(a => a.IsPinned).Take(5).Select(a => "Ссылка: " + a.Name));
        return items.Count == 0 ? "Пока ничего не закреплено." : string.Join("\n", items);
    }







    private void AddNote_Click(object sender, RoutedEventArgs e) => AddNote();
    private void AddConnection_Click(object sender, RoutedEventArgs e) => AddConnection();
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenSelectedFolder();
    private void DropZone_Click(object sender, RoutedEventArgs e) => ShowDropZone();































    private void OpenWorkspace()
    {
        var project = RequireProject();
        if (project is null) return;
        OpenWorkspace(project);
    }



    private async void BackupSelected()
    {
        var project = RequireProject();
        if (project is null) return;
        try
        {
            var result = await BackupService.CreateBackupAsync(project);
            WpfClipboard.SetText(result.ZipPath);
            AddLog("OK", $"Backup создан: {result.ZipPath} (+{result.NewFilesSincePrevious} новых файлов)");
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Backup: {ex.Message}");
        }
    }


    private void ShowGlobalSearch()
    {
        var window = new GlobalSearchWindow(SearchAll) { Owner = this };
        window.ShowDialog();
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

    private string LastBackupsText(ProjectProfile? project)
    {
        var backups = new List<string>();
        var sources = project is null ? _projects.Projects : [project];
        foreach (var item in sources)
        {
            var dir = Path.Combine(AppPaths.TodayWorkDay(item), "Backups");
            if (!Directory.Exists(dir)) continue;
            var zips = Directory.GetFiles(dir, "*.zip").OrderByDescending(File.GetLastWriteTime).Take(8).ToList();
            for (var i = 0; i < zips.Count; i++)
            {
                var zip = zips[i];
                var prevTime = i + 1 < zips.Count ? File.GetLastWriteTime(zips[i + 1]) : DateTime.MinValue;
                var newFiles = prevTime > DateTime.MinValue
                    ? BackupService.CountNewFilesSince(item, prevTime)
                    : -1;
                backups.Add(BackupService.FormatBackupLine(zip, newFiles));
            }
        }
        return backups.Count == 0 ? "Backup пока не найдены." : string.Join("\n", backups);
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
        _activityStore.Save(_activity);
    }

    private WpfBrush ThemeBorderMain() => (WpfBrush)FindResource("BorderMainBrush");

    private void RefreshCurrentView()
    {
        if (!string.IsNullOrWhiteSpace(_currentViewKey))
        {
            ActivateView(_currentViewKey, true);
            return;
        }

        ShowHome();
    }

    private Border Card(string title)
    {
        return new Border { Style = (Style)FindResource("Card"), Width = 340, MinHeight = 170 };
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

        var actionShell = new Border
        {
            Background = (WpfBrush)FindResource("PanelBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Margin = new Thickness(8, 8, 8, 14),
            Child = actions
        };
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

    private WpfButton FilterButton(string text, bool selected, Action action)
    {
        var button = new WpfButton
        {
            Content = text,
            Style = (Style)FindResource("GhostButton"),
            Height = 36,
            MinWidth = 86,
            MaxHeight = 36,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var bg = selected ? (WpfBrush)FindResource("FilterBgBrush") : (WpfBrush)FindResource("TealSoftBgBrush");
        var border = selected ? (WpfBrush)FindResource("FilterBorderBrush") : (WpfBrush)FindResource("TealBorderBrush");
        button.Background = bg;
        button.BorderBrush = border;
        button.Foreground = (WpfBrush)FindResource("TextBrush");
        button.MouseEnter += (_, _) =>
        {
            button.Background = selected ? (WpfBrush)FindResource("FilterBgBrush") : (WpfBrush)FindResource("TealSoftBgBrush");
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = bg;
        };
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
        card.Width = mode == ViewDisplayMode.Tile ? tileWidth : 760;
        card.MinHeight = mode == ViewDisplayMode.Tile ? 200 : 44;
    }

    private Border ListRow(string title, Action action, WpfBrush? marker = null, params UIElement[] actions)
    {
        var row = new Border
        {
            Background = (WpfBrush)FindResource("CardBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(8, 3, 8, 3),
            Width = 760,
            MinHeight = 42,
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
                ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF8C00"))
                : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#228B22"));
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


    private static bool IsTaskInProgress(TaskItem task) =>
        IsTaskRunning(task);

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
        if (action.Action is "portal" or "activate")
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

        ShowFromTray();
        switch (action.Action)
        {
            case "start":
                StartTask(task, openEditor: false);
                break;
            case "snooze15":
                SnoozeTask(task, TimeSpan.FromMinutes(15));
                break;
            default:
                EditTask(task);
                break;
        }
    }

    private void SnoozeTask(TaskItem task, TimeSpan snooze)
    {
        task.StartAt = DateTime.Now.Add(snooze);
        task.EndAt = task.StartAt.AddHours(1);
        task.ReminderAt = task.StartAt;
        task.LastNotifiedAt = null;
        task.Status = "Новая";
        _tasksStore.Save(_tasks);
        AddLog("OK", $"Задача отложена: {task.Title}");
        TaskNotificationService.ClearReminder(task.Id);
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
        stack.Children.Add(Text(title, 18, (WpfBrush)FindResource("TextBrush"), new Thickness(0, 0, 0, 12), FontWeights.SemiBold));
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
            MinWidth = 86,
            MaxHeight = 36,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(3, 3, 3, 3),
            VerticalAlignment = VerticalAlignment.Top
        };
        ApplyButtonTone(button, text, primary);
        button.Click += (_, _) => action();
        return button;
    }

    private void ApplyButtonTone(WpfButton button, string text, bool primary)
    {
        var lower = text.ToLowerInvariant();
        var bgKey = primary ? "ActionSoftBgBrush" : "PanelBrush";
        var borderKey = primary ? "ActionBorderBrush" : "AccentBorderBrush";

        if (lower.Contains("удал") || lower.Contains("очист") || lower.Contains("заверш"))
        {
            bgKey = "DangerSoftBgBrush";
            borderKey = "DangerBrush";
        }
        else if (lower.Contains("фокус") || lower.Contains("приступ") || lower.Contains("выполн") || lower.Contains("начать"))
        {
            bgKey = "FocusWarnBgBrush";
            borderKey = "WarnBrush";
        }
        else if ((lower.Contains("плит") || lower.Contains("спис")) && primary)
        {
            bgKey = "FilterBgBrush";
            borderKey = "FilterBorderBrush";
        }

        button.Background = (WpfBrush)FindResource(bgKey);
        button.BorderBrush = (WpfBrush)FindResource(borderKey);
        button.Foreground = (WpfBrush)FindResource("TextBrush");
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
            Height = 30,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => action();
        return button;
    }

    private WpfButton EditIconButton(Action action)
    {
        var button = IconButton("edit", action, "Редактировать", 28);
        button.Background = (WpfBrush)FindResource("PanelBrush");
        button.BorderBrush = (WpfBrush)FindResource("ActionBorderBrush");
        button.Foreground = (WpfBrush)FindResource("TextBrush");
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Thickness(0);
        return button;
    }

    private WpfButton IconButton(string iconName, Action action, string tooltip, double size = 34)
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
            BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#008B8B")),
            Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EEF3F7")),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Top
        };
        button.Click += (_, _) => action();
        return button;
    }

    private WpfButton FavoriteIconButton(bool isPinned, Action action, double rightOffset = 0)
    {
        var button = IconButton("star", action, isPinned ? "Убрать из избранного" : "Добавить в избранное", 28);
        button.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isPinned ? "#4A4618" : "#20262E"));
        button.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isPinned ? "#B8A900" : "#3A4451"));
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Thickness(0, 0, rightOffset, 0);
        return button;
    }

    private FrameworkElement MakeIcon(string iconName, double size)
    {
        var geometry = iconName switch
        {
            "edit" => "M3 17.25V21h3.75L17.8 9.95l-3.75-3.75L3 17.25z M20.7 7.05c.4-.4.4-1 0-1.4l-2.35-2.35c-.4-.4-1-.4-1.4 0l-1.85 1.85 3.75 3.75 1.85-1.85z",
            "grid" => "M3 3h7v7H3V3z M14 3h7v7h-7V3z M3 14h7v7H3v-7z M14 14h7v7h-7v-7z",
            "table" => "M4 5h16v2H4V5z M4 10h7v9H4v-9z M13 10h7v9h-7v-9z",
            "list" => "M4 6h16v2H4V6z M4 11h16v2H4v-2z M4 16h16v2H4v-2z",
            "back" => "M15.5 5l-7 7 7 7 1.5-1.5L11.5 12 17 6.5 15.5 5z",
            "forward" => "M8.5 5l7 7-7 7L7 17.5 12.5 12 7 6.5 8.5 5z",
            "close" => "M6.4 5L5 6.4 10.6 12 5 17.6 6.4 19 12 13.4 17.6 19 19 17.6 13.4 12 19 6.4 17.6 5 12 10.6 6.4 5z",
            "minus" => "M5 11h14v2H5v-2z",
            "maximize" => "M6 6h12v12H6V6z M8 8v8h8V8H8z",
            "restore" => "M7 7h10v3h-2V9H9v6h1v2H7V7z M11 11h8v8h-8v-8z M13 13v4h4v-4h-4z",
            "star" => "M12 2l2.9 6.2 6.8.8-5 4.7 1.3 6.7-6-3.4-6 3.4 1.3-6.7-5-4.7 6.8-.8L12 2z",
            "delete" => "M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12z M19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z",
            "play" => "M8 5v14l11-7L8 5z",
            "pause" => "M6 5h4v14H6V5z M14 5h4v14h-4V5z",
            "prev" => "M6 6h2v12H6V6z M20 6v12l-9-6 9-6z",
            "next" => "M16 6h2v12h-2V6z M4 6l9 6-9 6V6z",
            "music" => "M9 3v10.6a4 4 0 1 0 2 3.4V7h8V3H9z",
            "nav-home" => "M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z",
            "nav-projects" => "M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z",
            "nav-tasks" => "M9 16.2l-3.5-3.5L4 14.2l5 5 11-11-1.4-1.4L9 16.2z",
            "nav-notes" => "M4 4h12l4 4v12H4V4z M15 5v4h4 M7 12h10v2H7v-2z M7 15h7v2H7v-2z",
            "nav-connections" => "M3.9 12c0-1.7 1.3-3 3-3h2V7H6.9C4.8 7 3 8.8 3 10.9V12H1v2h2v1.1C3 17.2 4.8 19 6.9 19H9v-2H6.9c-1.7 0-3-1.3-3-3V12z M21 10.9C21 8.8 19.2 7 17.1 7H15v2h2.1c1.7 0 3 1.3 3 3V14h2v-1.1z M12 8l-4 4 4 4 4-4-4-4z",
            "nav-contacts" => "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z",
            "nav-browser" => "M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10 10-4.5 10-10S17.5 2 12 2zm-1 17.9C7.1 18.4 4 15.5 4 12c0-1.6.5-3.1 1.3-4.4L11 14v5.9zm6.9-2.5C16.9 16.9 14.5 18 12 18v-6l5.7-5.7c.8 1.3 1.3 2.8 1.3 4.4 0 1.8-.6 3.4-1.4 4.7z",
            "nav-commands" => "M8 5v14l11-7L8 5z M4 6h3v12H4V6z M17 6h3v12h-3V6z",
            "nav-backup" => "M19 12v7H5v-7H3v7c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2v-7h-2z M13 12.7l2.6-2.6 1.4 1.4L12 17 6 11l1.4-1.4 2.6 2.6V4h2v8.7z",
            "nav-dropzone" => "M12 2l6 8a6 6 0 1 1-12 0l6-8z",
            "nav-settings" => "M19.4 13a7.5 7.5 0 0 0 0-2l2-1.6-2-3.4-2.4 1a7.6 7.6 0 0 0-1.7-1L15 2h-4l-.3 2.4a7.6 7.6 0 0 0-1.7 1l-2.4-1-2 3.4 2 1.6a7.5 7.5 0 0 0 0 2l-2 1.6 2 3.4 2.4-1a7.6 7.6 0 0 0 1.7 1L11 22h4l.3-2.4a7.6 7.6 0 0 0 1.7-1l2.4 1 2-3.4-2-1.6z M12 15.5A3.5 3.5 0 1 1 12 8.5a3.5 3.5 0 0 1 0 7z",
            _ => "M4 4h16v16H4V4z"
        };
        var path = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse(geometry),
            Fill = (WpfBrush)FindResource("TextBrush"),
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size
        };
        return path;
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

    private static bool ClipboardHasBitmapWithoutText()
    {
        const uint cfUnicodeText = 13;
        const uint cfBitmap = 2;
        const uint cfDib = 8;
        if (IsClipboardFormatAvailable(cfUnicodeText) || IsClipboardFormatAvailable(1)) return false;
        return IsClipboardFormatAvailable(cfBitmap) || IsClipboardFormatAvailable(cfDib);
    }

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointNative ptReserved;
        public PointNative ptMaxSize;
        public PointNative ptMaxPosition;
        public PointNative ptMinTrackSize;
        public PointNative ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public RectNative rcMonitor;
        public RectNative rcWork;
        public int dwFlags;
    }
}

