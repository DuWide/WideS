using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private int _logSessionErrorCount;
    // Действия пунктов меню по порядку — для Ctrl+1…9.
    private readonly List<Action> _navActions = new();
    // Разделители групп меню, видимые только в компактной панели (там скрыты заголовки групп).
    private readonly List<FrameworkElement> _navCompactDividers = new();
    private string _breadcrumb = "";
    private string _taskTab = "Все";
    private string _projectStatusFilter = "Active";
    private readonly List<TextBlock> _navGroups = [];
    private readonly Dictionary<string, TextBlock> _navLabels = new(StringComparer.OrdinalIgnoreCase);

    private void AfterLoadData()
    {
        var smokeTheme = Environment.GetEnvironmentVariable("WIDES_THEME");
        ApplyTheme(string.IsNullOrWhiteSpace(smokeTheme) ? _settings.AccentTheme : smokeTheme);
        UpdateResponsiveSidebar();
    }

    /// <summary>В узком окне (половина экрана) полная боковая панель съедает слишком много места.</summary>
    private const double CompactSidebarWidthThreshold = 940;

    private bool? _compactSidebarApplied;

    private void UpdateResponsiveSidebar()
    {
        if (_immersiveMode)
        {
            // В режиме чтения боковая панель скрыта; ширину восстановим при выходе.
            return;
        }

        var width = IsLoaded ? ActualWidth : Width;
        var compact = _settings.CompactSidebar || width < CompactSidebarWidthThreshold;
        if (_compactSidebarApplied == compact)
        {
            return;
        }

        _compactSidebarApplied = compact;
        ApplyCompactSidebar(compact);
    }

    private void ApplyCompactSidebar(bool compact)
    {
        SidebarColumn.Width = new GridLength(compact ? 64 : 220);
        SidebarInnerGrid.Margin = compact
            ? new Thickness(4, 10, 4, 8)
            : new Thickness(12, 14, 12, 12);

        foreach (var group in _navGroups)
        {
            group.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }

        foreach (var divider in _navCompactDividers)
        {
            divider.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var (key, button) in _sideNavButtons)
        {
            if (_navLabels.TryGetValue(key, out var label))
            {
                label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            }

            button.MinWidth = 0;
            button.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = compact
                ? System.Windows.HorizontalAlignment.Center
                : System.Windows.HorizontalAlignment.Left;
            button.Padding = compact ? new Thickness(4, 8, 4, 8) : new Thickness(10, 7, 10, 7);
            button.Margin = compact ? new Thickness(0, 2, 0, 2) : new Thickness(0, 1, 0, 1);
        }

        SidebarFooterPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        UpdateNavHighlight();
    }

    private void UpdateLogPreview()
    {
        JournalBadge.Visibility = _logSessionCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        JournalBadgeText.Text = _logSessionCount > 99 ? "99+" : _logSessionCount.ToString();
        // Обычные записи — нейтральный серый счётчик; красный только при ошибках.
        var hasErrors = _logSessionErrorCount > 0;
        JournalBadge.SetResourceReference(Border.BackgroundProperty, hasErrors ? "DangerBrush" : "ElevatedBgBrush");
        JournalBadgeText.SetResourceReference(TextBlock.ForegroundProperty, hasErrors ? "AccentForegroundBrush" : "MutedBrush");
        if (_logSessionCount > 0)
        {
            JournalButton.ToolTip = hasErrors
                ? $"Журнал действий ({_logSessionCount} новых, ошибок: {_logSessionErrorCount})"
                : $"Журнал действий ({_logSessionCount} новых)";
        }
        else
        {
            JournalButton.ToolTip = "Журнал действий";
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
        if (NavPanel.Children.Count > 0)
        {
            var divider = new Border
            {
                Height = 1,
                Margin = new Thickness(12, 6, 12, 6),
                Opacity = 0.3,
                Visibility = Visibility.Collapsed
            };
            divider.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
            _navCompactDividers.Add(divider);
            NavPanel.Children.Add(divider);
        }

        NavPanel.Children.Add(header);
    }

    private void BuildNavGrouped()
    {
        NavPanel.Children.Clear();
        NavBottomPanel.Children.Clear();
        _sideNavButtons.Clear();
        _navGroups.Clear();
        _navLabels.Clear();
        _navActions.Clear();
        _navCompactDividers.Clear();

        AddNavGroup("РАБОЧЕЕ ПРОСТРАНСТВО");
        AddNav("Проекты", "projects", ShowProjects);
        AddNav("Заметки", "notes", ShowNotes);
        AddNav("Подключения", "connections", ShowConnections);
        AddNav("Задачи", "tasks", ShowTasks);
        AddNavGroup("МЕДИА");
        AddNav("Мессенджеры", "messengers", ShowMessengers);
        AddNav("Музыка", "music", ShowMusic);
        AddNav("TikTok", "tiktok", ShowTikTok);
        AddNav("Манга", "manga", ShowManga);
        AddNav("Настройки", "settings", ShowSettings, NavBottomPanel);
        UpdateNavHighlight();
        _compactSidebarApplied = null;
        UpdateResponsiveSidebar();
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
        _breadcrumb = breadcrumb ?? title;
        PageTitle.Text = title;
        PageTitle.ToolTip = string.IsNullOrWhiteSpace(subtitle) ? _breadcrumb : subtitle;
    }

    private Storyboard? _raycastGlowStoryboard;
    private bool _glowAnimationRunning;

    /// <summary>
    /// Пульсация свечения темы Raycast — бесконечная анимация поверх размытых слоёв на всё окно:
    /// каждый её кадр заставляет перерисовывать весь интерфейс. Поэтому она идёт только когда окно
    /// реально видно и не включён режим производительности; иначе свечение остаётся статичным.
    /// </summary>
    private void UpdateGlowAnimationState()
    {
        if (_raycastGlowStoryboard is null && AmbientGlowLayer.Resources["RaycastGlowPulse"] is Storyboard sb)
        {
            _raycastGlowStoryboard = sb;
        }

        if (_raycastGlowStoryboard is null)
        {
            return;
        }

        var shouldRun = AmbientGlowLayer.Visibility == Visibility.Visible &&
                        !PerformanceProfile.ReducedEffects &&
                        IsVisible &&
                        WindowState != WindowState.Minimized;
        if (shouldRun == _glowAnimationRunning)
        {
            return;
        }

        _glowAnimationRunning = shouldRun;
        if (shouldRun)
        {
            _raycastGlowStoryboard.Begin(AmbientGlowLayer, true);
        }
        else
        {
            _raycastGlowStoryboard.Stop(AmbientGlowLayer);
            AmbientGlowLayer.Opacity = 0.85;
            GlowParallax.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            GlowParallax.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            GlowParallax.X = 0;
            GlowParallax.Y = 0;
        }
    }

    private long _lastGlowParallaxTick;

    /// <summary>
    /// Лёгкий параллакс: свечение плавно смещается за курсором (до ±18 px), будто лежит глубже интерфейса.
    /// Обновление не чаще 8 раз в секунду, короткая анимация с малой частотой кадров — почти бесплатно.
    /// </summary>
    private void OnGlowMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_glowAnimationRunning || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastGlowParallaxTick < 120)
        {
            return;
        }

        _lastGlowParallaxTick = now;
        var p = e.GetPosition(this);
        var dx = (p.X / ActualWidth - 0.5) * 36;
        var dy = (p.Y / ActualHeight - 0.5) * 24;
        AnimateParallax(System.Windows.Media.TranslateTransform.XProperty, dx);
        AnimateParallax(System.Windows.Media.TranslateTransform.YProperty, dy);
    }

    private void AnimateParallax(DependencyProperty property, double to)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        Timeline.SetDesiredFrameRate(animation, 30);
        animation.Freeze();
        GlowParallax.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyTheme(string theme)
    {
        ThemeService.Apply(theme);
        var raycast = ThemeService.UsesAmbientGlow(theme);
        AmbientGlowLayer.Visibility = raycast ? Visibility.Visible : Visibility.Collapsed;

        _glowAnimationRunning = false;
        _raycastGlowStoryboard?.Stop(AmbientGlowLayer);
        UpdateGlowAnimationState();

        // Raycast UX: воздух + просвечивающий контент; обычные темы — как раньше.
        ContentScrollViewer.Background = System.Windows.Media.Brushes.Transparent;
        ContentScrollViewer.Padding = raycast
            ? new Thickness(28, 20, 28, 22)
            : new Thickness(24, 16, 24, 18);
        RootChromeBorder.BorderThickness = raycast ? new Thickness(0) : new Thickness(1);
        if (FindName("SidebarChromeBorder") is Border sidebarChrome)
            sidebarChrome.BorderThickness = raycast ? new Thickness(0) : new Thickness(0, 0, 1, 0);
        if (FindName("TitleBarBorder") is Border titleBar)
            titleBar.BorderThickness = raycast ? new Thickness(0) : new Thickness(0, 0, 0, 1);
        if (FindName("NowPlayingPanel") is Border np)
        {
            np.CornerRadius = new CornerRadius(raycast ? 10 : 7);
            np.BorderThickness = new Thickness(0);
        }

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
