using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfMessageBox = System.Windows.MessageBox;
using WpfClipboard = System.Windows.Clipboard;

namespace DevCockpit;

public partial class MainWindow
{
    private int _logSessionCount;
    private List<string> _lastDropBatch = [];
    private string _breadcrumb = "";
    private bool _updatingProjectSwitcher;
    private Guid? _taskProjectFilter;
    private bool _includeProjectTasks;
    private string? _contactFilterKey;
    private string? _contactFilterLabel;
    private string _taskImportanceFilter = "Все";
    private string _noteTagFilter = "";
    private string _projectStatusFilter = "Active";
    private bool _logExpanded;
    private readonly List<TextBlock> _navGroups = [];
    private readonly Dictionary<string, TextBlock> _navLabels = new(StringComparer.OrdinalIgnoreCase);

    private void AfterLoadData()
    {
        RefreshProjectSwitcher();
        ApplyTheme(_settings.AccentTheme);
        ApplyCompactSidebar(_settings.CompactSidebar);
    }

    private void ApplyCompactSidebar(bool compact)
    {
        SidebarColumn.Width = new GridLength(compact ? 56 : 200);
        foreach (var group in _navGroups)
        {
            group.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }

        foreach (var (key, button) in _sideNavButtons)
        {
            if (_navLabels.TryGetValue(key, out var label))
            {
                label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            }

            button.HorizontalContentAlignment = compact
                ? System.Windows.HorizontalAlignment.Center
                : System.Windows.HorizontalAlignment.Left;
            button.Padding = compact ? new Thickness(8, 9, 8, 9) : new Thickness(12, 9, 12, 9);
        }

        SidebarFooterPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NpSourceRow.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NpTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NpArtist.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NpContentPanel.HorizontalAlignment = compact
            ? System.Windows.HorizontalAlignment.Center
            : System.Windows.HorizontalAlignment.Stretch;
        NpControls.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        NowPlayingPanel.Padding = compact ? new Thickness(6, 10, 6, 10) : new Thickness(10, 9, 10, 9);
        NowPlayingPanel.HorizontalAlignment = compact
            ? System.Windows.HorizontalAlignment.Center
            : System.Windows.HorizontalAlignment.Stretch;
        if (compact)
        {
            NpAlbumArt.Visibility = Visibility.Collapsed;
        }
    }

    private void AddNavGroup(string title)
    {
        var header = new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 85, 104)),
            Margin = new Thickness(8, 10, 0, 6)
        };
        _navGroups.Add(header);
        NavPanel.Children.Add(header);
    }

    private void BuildNavGrouped()
    {
        NavPanel.Children.Clear();
        TopNavPanel.Children.Clear();
        _sideNavButtons.Clear();
        _topNavButtons.Clear();
        _navGroups.Clear();
        _navLabels.Clear();

        AddNavGroup("РАБОТА");
        AddNav("Главная", "home", ShowHome);
        AddNav("Проекты", "projects", ShowProjects);
        AddNav("Задачи", "tasks", ShowTasks);
        AddNav("Клиенты", "contacts", ShowContacts);
        AddNav("Заметки", "notes", ShowNotes);
        AddNav("Подключения", "connections", ShowConnections);
        AddNav("Браузер", "ai", ShowAiAgents);

        AddNavGroup("ИНСТРУМЕНТЫ");
        AddNav("Команды", "commands", ShowCommandRecipes);
        AddNav("Backup", "backup", ShowBackupContext);
        AddNav("DropZone", "dropzone", ShowDropZone);

        AddNavGroup("СИСТЕМА");
        AddNav("Настройки", "settings", ShowSettings);

        AddTopNav("Избранное", "favorites", ShowFavorites);
        UpdateNavHighlight();
        ApplyCompactSidebar(_settings.CompactSidebar);
    }

    private void SetProjectStatus(ProjectProfile project, string status)
    {
        project.Status = status;
        _projectStore.Save(_projects);
        AddLog("OK", $"Статус «{project.Name}»: {UiHelpers.ProjectStatusDisplay(status)}");
        RefreshProjectSwitcher();
        if (_currentViewKey == "projects") ShowProjects();
        else RefreshAfterPinnedChange();
    }

    private void SetTitle(string title, string subtitle, string? breadcrumb = null)
    {
        PageTitle.Text = title;
        _breadcrumb = breadcrumb ?? title;
        PageSubtitle.Text = string.IsNullOrWhiteSpace(subtitle)
            ? _breadcrumb
            : $"{_breadcrumb}  ›  {subtitle}";
    }

    private void RefreshProjectSwitcher()
    {
        _updatingProjectSwitcher = true;
        ProjectSwitcher.ItemsSource = null;
        var items = _projects.Projects
            .Where(p => !p.Status.Equals("Archive", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.CreatedAt)
            .Prepend(new ProjectProfile { Id = Guid.Empty, Name = "(выберите проект)" })
            .ToList();
        ProjectSwitcher.ItemsSource = items;
        ProjectSwitcher.DisplayMemberPath = nameof(ProjectProfile.Name);
        if (_selectedProject is not null)
        {
            ProjectSwitcher.SelectedItem = items.FirstOrDefault(p => p.Id == _selectedProject.Id) ?? items[0];
        }
        else
        {
            ProjectSwitcher.SelectedIndex = 0;
        }
        _updatingProjectSwitcher = false;
    }

    private void ProjectSwitcher_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProjectSwitcher) return;
        if (ProjectSwitcher.SelectedItem is not ProjectProfile project || project.Id == Guid.Empty)
        {
            _selectedProject = null;
            PauseRunningTaskIfProjectChanged(null);
            return;
        }

        _selectedProject = project;
        PauseRunningTaskIfProjectChanged(project);
        project.LastOpenedAt = DateTime.Now;
        _projectStore.Save(_projects);
        RenderTabs();
    }

    private void UpdateLogPreview()
    {
        var text = LogBox.Text;
        var lastLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Нет записей";
        LogPreview.Text = lastLine.Trim();
        LogBadge.Visibility = _logSessionCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        LogBadgeText.Text = _logSessionCount.ToString();
    }

    private void ApplyTheme(string theme)
    {
        ThemeService.Apply(theme);
    }

    private IEnumerable<TaskItem> FilteredTasks(bool archive)
    {
        return _tasks.Tasks
            .Where(t => archive ? t.IsDone : !t.IsDone)
            .Where(t => _taskProjectFilter is not null
                ? t.WorkspaceId == _taskProjectFilter
                : _includeProjectTasks || !HasTaskProject(t))
            .Where(t => _contactFilterKey is null || ContactAggregator.MatchesKey(t, _contactFilterKey))
            .Where(t => _taskImportanceFilter == "Все" || t.Importance.Equals(_taskImportanceFilter, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasTaskProject(TaskItem task) =>
        task.WorkspaceId is { } workspaceId && workspaceId != Guid.Empty;

    private static string TaskGroupKey(TaskItem task, DateTime today)
    {
        if (IsTaskRunning(task)) return "В работе";
        if (IsTaskPaused(task)) return "На паузе";
        if (task.StartAt.Date < today) return "Просрочено";
        if (task.StartAt.Date == today) return "Сегодня";
        if (task.StartAt.Date == today.AddDays(1)) return "Завтра";
        if (task.StartAt.Year <= 2000) return "Без даты";
        return "Позже";
    }

    private Border TaskGroupHeader(string title, bool stretch = false)
    {
        return new Border
        {
            Width = stretch ? double.NaN : 760,
            HorizontalAlignment = stretch
                ? System.Windows.HorizontalAlignment.Stretch
                : System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(8, 14, 8, 6),
            Child = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (WpfBrush)FindResource("AccentBrush")
            }
        };
    }

    private void CompleteTaskWithRecurrence(TaskItem task)
    {
        if (!task.Recurrence.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            var next = task.Recurrence.Equals("Weekly", StringComparison.OrdinalIgnoreCase)
                ? task.StartAt.AddDays(7)
                : task.StartAt.AddDays(1);
            var clone = new TaskItem
            {
                Title = task.Title,
                Description = task.Description,
                Importance = task.Importance,
                WorkspaceId = task.WorkspaceId,
                StartAt = next,
                EndAt = next.AddHours(1),
                ReminderAt = next,
                Recurrence = task.Recurrence,
                Status = "Новая",
                CreatedAt = DateTime.Now
            };
            _tasks.Tasks.Add(clone);
        }

        ArchiveTask(task);
    }

    private void TouchProjectOpened(ProjectProfile? project)
    {
        if (project is null) return;
        project.LastOpenedAt = DateTime.Now;
        _projectStore.Save(_projects);
    }

    private bool EnsureProjectSelected(string action)
    {
        if (_selectedProject is not null && _selectedProject.Id != Guid.Empty) return true;
        WpfMessageBox.Show(this, $"Сначала выберите проект в верхней панели.\n{action}", "WideS");
        return false;
    }

    private void UndoLastDrop()
    {
        if (_lastDropBatch.Count == 0) return;
        foreach (var path in _lastDropBatch)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }

        AddLog("OK", $"DropZone: отменено файлов {_lastDropBatch.Count}.");
        _lastDropBatch.Clear();
        if (_currentViewKey == "dropzone") ShowDropZone();
    }

    private string DropTargetPreview()
    {
        if (_selectedProject is null) return "Проект не выбран";
        return AppPaths.GetDropZoneFolder(_selectedProject);
    }

    private Border FavoriteUnifiedRow(string type, string title, string subtitle, Action open, Action? pinToggle = null)
    {
        var row = new Border
        {
            Background = (WpfBrush)FindResource("CardBrush"),
            BorderBrush = (WpfBrush)FindResource("AccentBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(8, 4, 8, 4),
            Width = 760,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = UiHelpers.TypeBadge(type);
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);
        var text = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = (WpfBrush)FindResource("TextBrush") });
        text.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = (WpfBrush)FindResource("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (pinToggle is not null)
        {
            var star = ActionButton("★", pinToggle, false);
            star.Width = 36;
            Grid.SetColumn(star, 2);
            grid.Children.Add(star);
        }
        row.Child = grid;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject)) open();
        };
        return row;
    }

    private void RunHomeQuickAction(string key)
    {
        switch (key)
        {
            case "note": AddNote(); break;
            case "task": AddTask(); break;
            case "connection": AddConnection(); break;
            case "command": AddCommandRecipe(); break;
            case "backup": BackupSelected(); break;
            case "context": CopyForAiSelected(); break;
            case "report": BuildDailyReport(); break;
            case "dropzone": ShowDropZone(); break;
            case "project": ShowProjects(); break;
            case "cursor": OpenWorkspace(); break;
            case "browser": ShowAiAgents(); break;
            case "dock": ToggleDock(); break;
        }
    }

    private async void StartReachabilityIndicator(ConnectionItem item, TextBlock indicator)
    {
        indicator.Text = "…";
        var ok = await UiHelpers.CheckReachabilityAsync(item.Address, item.Type);
        indicator.Text = ok == true ? "● online" : ok == false ? "○ offline" : "";
        indicator.Foreground = ok == true
            ? (WpfBrush)FindResource("AccentBrush")
            : (WpfBrush)FindResource("MutedBrush");
    }

    private void ExportData()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "ZIP archive|*.zip",
            FileName = $"WideS-backup-{DateTime.Now:yyyyMMdd}.zip"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName);
            System.IO.Compression.ZipFile.CreateFromDirectory(AppPaths.DataDirectory, dialog.FileName);
            AddLog("OK", $"Экспорт данных: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            AddLog("ERR", ex.Message);
        }
    }
}
