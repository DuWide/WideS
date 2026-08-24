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
    private string _breadcrumb = "";
    private string _taskTab = "Все";
    private string _projectStatusFilter = "Active";
    private bool _logExpanded;
    private readonly List<TextBlock> _navGroups = [];
    private readonly Dictionary<string, TextBlock> _navLabels = new(StringComparer.OrdinalIgnoreCase);

    private void AfterLoadData()
    {
        var smokeTheme = Environment.GetEnvironmentVariable("WIDES_THEME");
        ApplyTheme(string.IsNullOrWhiteSpace(smokeTheme) ? _settings.AccentTheme : smokeTheme);
        ApplyCompactSidebar(_settings.CompactSidebar);
    }

    private void ApplyCompactSidebar(bool compact)
    {
        SidebarColumn.Width = new GridLength(compact ? 64 : 220);
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
            button.Padding = compact ? new Thickness(8, 8, 8, 8) : new Thickness(10, 7, 10, 7);
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
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("SubtleBrush"),
            Margin = new Thickness(10, 12, 0, 5)
        };
        _navGroups.Add(header);
        NavPanel.Children.Add(header);
    }

    private void BuildNavGrouped()
    {
        NavPanel.Children.Clear();
        _sideNavButtons.Clear();
        _navGroups.Clear();
        _navLabels.Clear();

        AddNavGroup("РАБОЧЕЕ ПРОСТРАНСТВО");
        AddNav("Проекты", "projects", ShowProjects);
        AddNav("Заметки", "notes", ShowNotes);
        AddNav("Подключения", "connections", ShowConnections);
        AddNav("Задачи", "tasks", ShowTasks);
        AddNav("Мессенджеры", "messengers", ShowMessengers);
        AddNav("Музыка", "music", ShowMusic);
        AddNav("Настройки", "settings", ShowSettings);
        UpdateNavHighlight();
        ApplyCompactSidebar(_settings.CompactSidebar);
    }

    private void SetProjectStatus(ProjectProfile project, string status)
    {
        project.Status = status;
        _projectStore.Save(_projects);
        AddLog("OK", $"Статус «{project.Name}»: {UiHelpers.ProjectStatusDisplay(status)}");
        if (_currentViewKey == "projects") ShowProjects();
        else RefreshCurrentView();
    }

    private void SetTitle(string title, string subtitle, string? breadcrumb = null)
    {
        UseStandardContentLayout();
        ContentScrollViewer.ScrollToTop();
        PageTitle.Text = title;
        _breadcrumb = breadcrumb ?? title;
        PageSubtitle.Text = string.IsNullOrWhiteSpace(subtitle) ? _breadcrumb : subtitle;
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
        _dockWindow?.RefreshTheme();
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
            FileName = $"WideS-data-export-{DateTime.Now:yyyyMMdd}.zip"
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
