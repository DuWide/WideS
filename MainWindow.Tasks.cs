using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using Forms = System.Windows.Forms;

namespace DevCockpit;

public partial class MainWindow
{
    private void ShowTasks()
    {
        EnterView("tasks");
        _viewScope = "tasks";
        SetTitle("Задачи", "Все задачи с датами и напоминаниями");
        var root = new DockPanel();
        var toolbar = UiHelpers.ToolbarRow();
        var list = new StackPanel { Margin = new Thickness(0) };

        void Render()
        {
            list.Children.Clear();
            var tasks = _tasks.Tasks
                .Where(t => _taskTab switch
                {
                    "В работе" => !t.IsDone && (IsTaskRunning(t) || IsTaskPaused(t)),
                    "Запланировано" => !t.IsDone && !IsTaskRunning(t) && !IsTaskPaused(t),
                    "Выполнено" => t.IsDone,
                    _ => true
                })
                .OrderBy(t => t.IsDone)
                .ThenByDescending(t => t.StartAt)
                .ToList();

            if (tasks.Count == 0)
            {
                list.Children.Add(UiHelpers.EmptyState("Задач нет", "В этой категории пока нет задач.", "Новая задача", () => AddTask()));
                return;
            }

            if (IsRaycastUi)
            {
                foreach (var task in tasks)
                {
                    list.Children.Add(TaskRaycastRow(task));
                }
            }
            else
            {
                list.Children.Add(TaskTableHeader());
                foreach (var task in tasks)
                {
                    list.Children.Add(TaskCompactRow(task));
                }
            }
        }

        toolbar.Children.Add(ActionButton("Новая задача", () => AddTask()));
        if (IsRaycastUi)
        {
            toolbar.Children.Add(ToolbarGap(10));
        }

        foreach (var tab in new[] { "Все", "В работе", "Запланировано", "Выполнено" })
        {
            toolbar.Children.Add(FilterButton(tab, _taskTab == tab, () =>
            {
                _taskTab = tab;
                ShowTasks();
            }));
        }

        var toolbarShell = new Border
        {
            Style = (Style)FindResource("SectionToolbar"),
            Child = toolbar
        };
        if (IsRaycastUi)
        {
            toolbarShell.Background = (WpfBrush)FindResource("PanelBrush");
            toolbarShell.CornerRadius = new CornerRadius(12);
            toolbarShell.BorderThickness = new Thickness(0);
            toolbarShell.Padding = new Thickness(10, 8, 10, 8);
        }

        DockPanel.SetDock(toolbarShell, Dock.Top);
        root.Children.Add(toolbarShell);
        Render();
        root.Children.Add(list);
        ContentHost.Content = root;
    }
    private void AddTask(bool forceCommon = false)
    {
        var win = new TaskEditorWindow(null) { Owner = this };
        EditorWindowHelper.Register(win.Task.Id, win);
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            win.Task.CreatedAt = DateTime.Now;
            _tasks.Tasks.Add(win.Task);
            _tasksStore.Save(_tasks);
            AddLog("OK", $"Задача добавлена: {win.Task.Title}");
            RefreshAfterTaskChange(win.Task);
        };
        EditorWindowHelper.ShowNearOwner(win);
    }
    private void EditTask(TaskItem task)
    {
        if (EditorWindowHelper.TryActivate(task.Id)) return;

        var win = new TaskEditorWindow(task) { Owner = this };
        EditorWindowHelper.Register(task.Id, win);
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            var index = _tasks.Tasks.FindIndex(t => t.Id == task.Id);
            if (index >= 0) _tasks.Tasks[index] = win.Task;
            _tasksStore.Save(_tasks);
            AddLog("OK", $"Задача изменена: {win.Task.Title}");
            RefreshAfterTaskChange(win.Task);
        };
        EditorWindowHelper.ShowNearOwner(win);
    }
    private void StartTask(TaskItem task, bool openEditor = false)
    {
        ClearOtherRunningTasks(task.Id);
        task.IsDone = false;
        task.Status = "Выполняется";
        task.WorkStartedAt ??= DateTime.Now;
        task.ReminderAt = null;
        task.LastNotifiedAt = null;
        _trackedTaskId = task.Id;
        _tasksStore.Save(_tasks);
        AddLog("OK", $"Задача в работе: {task.Title}");
        TaskNotificationService.ClearReminder(task.Id);
        UpdateActiveTaskPill();
        if (openEditor)
        {
            EditTask(task);
            return;
        }

        RefreshTasksView();
    }
    private void PauseTask(TaskItem task)
    {
        if (!IsTaskRunning(task)) return;
        task.Status = "На паузе";
        _trackedTaskId = task.Id;
        _tasksStore.Save(_tasks);
        AddLog("OK", $"Задача на паузе: {task.Title}");
        UpdateActiveTaskPill();
        RefreshTasksView();
    }
    private void RefreshTasksView()
    {
        if (_currentViewKey == "tasks")
        {
            ShowTasks();
            return;
        }

    }
    private void ArchiveTask(TaskItem task)
    {
        task.IsDone = true;
        task.Status = "Архив";
        task.WorkStartedAt = null;
        task.ReminderAt = null;
        task.LastNotifiedAt = null;
        if (_trackedTaskId == task.Id) _trackedTaskId = null;
        _tasksStore.Save(_tasks);
        AddLog("OK", $"Задача завершена и отправлена в архив: {task.Title}");
        UpdateActiveTaskPill();
        RefreshTasksView();
    }
    private void RestoreTask(TaskItem task)
    {
        task.IsDone = false;
        task.Status = "Новая";
        _tasksStore.Save(_tasks);
        AddLog("OK", $"Задача возвращена из архива: {task.Title}");
        ShowTasks();
    }
    private void DeleteTask(TaskItem task)
    {
        if (WpfMessageBox.Show(this, $"Удалить задачу \"{task.Title}\"?", "WideS", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        EditorWindowHelper.CloseRegistered(task.Id);
        _tasks.Tasks.Remove(task);
        _tasksStore.Save(_tasks);
        AddLog("WARN", $"Задача удалена: {task.Title}");
        ShowTasks();
    }
    private void RefreshAfterTaskChange(TaskItem task)
    {
        ShowTasks();
    }
    private void CheckTaskReminders()
    {
        if (_activeReminderWindow is not null) return;

        var now = DateTime.Now;
        var task = _tasks.Tasks.FirstOrDefault(t => ShouldRemindTask(t, now));
        if (task is null) return;

        task.LastNotifiedAt = now;
        _tasksStore.Save(_tasks);

        if (_settings.ToastNotificationsEnabled && ShouldUseToastReminder())
        {
            TaskNotificationService.ShowReminder(task);
            return;
        }

        var win = new TaskReminderWindow(task);
        _activeReminderWindow = win;
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            win.Owner = this;
        }

        win.Closed += (_, _) =>
        {
            _activeReminderWindow = null;
            if (win.StartRequested)
            {
                StartTask(task, openEditor: false);
            }
            else if (win.Snooze is { } snooze)
            {
                SnoozeTask(task, snooze);
            }

            _tasksStore.Save(_tasks);
        };
        win.Show();
    }

    private void ClearOtherRunningTasks(Guid activeTaskId)
    {
        foreach (var other in _tasks.Tasks.Where(t => t.Id != activeTaskId && IsTaskRunning(t)))
        {
            other.Status = "На паузе";
        }
    }

    private void UpdateActiveTaskPill()
    {
        var task = _tasks.Tasks.FirstOrDefault(IsTaskRunning)
                   ?? (_trackedTaskId is { } trackedId
                       ? _tasks.Tasks.FirstOrDefault(t => t.Id == trackedId && IsTaskPaused(t))
                       : null);
        if (task is null)
        {
            ActiveTaskPill.Visibility = Visibility.Collapsed;
            _activeTaskPillId = null;
            _pillTimer.Stop();
            return;
        }

        if (IsTaskRunning(task) && task.WorkStartedAt is null)
        {
            task.WorkStartedAt = DateTime.Now;
        }

        _activeTaskPillId = task.Id;
        _trackedTaskId = task.Id;
        ActiveTaskPill.Visibility = Visibility.Visible;
        var icon = IsTaskRunning(task) ? "⏸" : "▶";
        ActiveTaskPillText.Text = $"{icon} {Preview(task.Title, 28)} · {FormatTaskDuration(task.WorkStartedAt)}";
        if (IsTaskRunning(task))
        {
            _pillTimer.Start();
        }
        else
        {
            _pillTimer.Stop();
        }
    }

    private void ActiveTaskPill_Click(object sender, MouseButtonEventArgs e)
    {
        if (_activeTaskPillId is not { } taskId)
        {
            return;
        }

        var task = _tasks.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
        {
            UpdateActiveTaskPill();
            return;
        }

        if (IsTaskRunning(task))
        {
            PauseTask(task);
            return;
        }

        if (IsTaskPaused(task))
        {
            StartTask(task, openEditor: false);
        }
    }

    private static string FormatTaskDuration(DateTime? startedAt)
    {
        if (startedAt is null)
        {
            return "0м";
        }

        var span = DateTime.Now - startedAt.Value;
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}ч {span.Minutes}м";
        }

        return $"{Math.Max(0, (int)span.TotalMinutes)}м";
    }
    private Border TaskTableHeader() => BuildTableHeader(
        ("Задача", new GridLength(2, GridUnitType.Star)),
        ("Дата", new GridLength(110)),
        ("Статус", new GridLength(100)),
        ("Действия", GridLength.Auto));

    /// <summary>Raycast-стиль: отдельные стеклянные строки, одна главная кнопка + меню «⋯».</summary>
    private Border TaskRaycastRow(TaskItem task)
    {
        var row = new Border
        {
            Background = (WpfBrush)FindResource("CardBrush"),
            BorderBrush = (WpfBrush)FindResource("BorderSubtleBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 10, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        var titleRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        titleRow.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = ImportanceBrush(task.Importance),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = task.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        left.Children.Add(titleRow);

        var meta = new TextBlock
        {
            Text = $"{(task.StartAt.Year <= 2000 ? "Без даты" : task.StartAt.ToString("dd.MM.yyyy HH:mm"))}  ·  {(IsTaskRunning(task) ? "В работе" : IsTaskPaused(task) ? "На паузе" : TaskStatusText(task))}",
            FontSize = 11,
            Foreground = (WpfBrush)FindResource("MutedBrush"),
            Margin = new Thickness(16, 4, 0, 0)
        };
        left.Children.Add(meta);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var menuItems = new List<(string Label, Action Run)>();
        if (task.IsDone)
        {
            actions.Children.Add(CompactActionButton("Вернуть", () => RestoreTask(task)));
        }
        else if (IsTaskRunning(task))
        {
            actions.Children.Add(CompactActionButton("Пауза", () => PauseTask(task)));
            menuItems.Add(("Готово", () => CompleteTaskWithRecurrence(task)));
        }
        else if (IsTaskPaused(task))
        {
            actions.Children.Add(CompactActionButton("Далее", () => StartTask(task, openEditor: false)));
            menuItems.Add(("Готово", () => CompleteTaskWithRecurrence(task)));
        }
        else
        {
            actions.Children.Add(CompactActionButton("Начать", () => StartTask(task)));
            menuItems.Add(("Готово", () => CompleteTaskWithRecurrence(task)));
        }

        menuItems.Add(("Изменить", () => EditTask(task)));
        actions.Children.Add(RowOverflowButton(menuItems.ToArray()));
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        row.Child = grid;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                EditTask(task);
            }
        };
        return row;
    }

    private WpfButton RowOverflowButton(params (string Label, Action Run)[] items)
    {
        var button = new WpfButton
        {
            Content = MakeIcon("more", 14),
            Style = (Style)FindResource("WindowButton"),
            Width = 30,
            Height = 28,
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = "Ещё действия",
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var menu = new ContextMenu();
        foreach (var (label, run) in items)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }

        button.Click += (_, e) =>
        {
            e.Handled = true;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        };
        return button;
    }

    private Border TaskCompactRow(TaskItem task)
    {
        var grid = CreateTableGrid(
            new GridLength(2, GridUnitType.Star),
            new GridLength(110),
            new GridLength(100),
            GridLength.Auto);

        var titlePanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        titlePanel.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = ImportanceBrush(task.Importance),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = task.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = task.Title
        });
        AddCell(grid, 0, titlePanel);
        AddCell(grid, 1, Muted(task.StartAt.Year <= 2000 ? "—" : task.StartAt.ToString("dd.MM.yyyy HH:mm")));

        var statusText = IsTaskRunning(task) ? "В работе" : IsTaskPaused(task) ? "На паузе" : TaskStatusText(task);
        AddCell(grid, 2, Muted(statusText));

        var actions = CompactRowActions();
        if (task.IsDone)
        {
            actions.Children.Add(CompactActionButton("Вернуть", () => RestoreTask(task)));
        }
        else if (IsTaskRunning(task))
        {
            actions.Children.Add(CompactActionButton("Пауза", () => PauseTask(task)));
            actions.Children.Add(CompactActionButton("Готово", () => CompleteTaskWithRecurrence(task)));
        }
        else if (IsTaskPaused(task))
        {
            actions.Children.Add(CompactActionButton("Далее", () => StartTask(task, openEditor: false)));
            actions.Children.Add(CompactActionButton("Готово", () => CompleteTaskWithRecurrence(task)));
        }
        else
        {
            actions.Children.Add(CompactActionButton("Начать", () => StartTask(task)));
            actions.Children.Add(CompactActionButton("Готово", () => CompleteTaskWithRecurrence(task)));
        }

        actions.Children.Add(CompactIconButton(EditIconButton(() => EditTask(task))));
        AddCell(grid, 3, actions);

        return WrapTableRow(grid, () => EditTask(task));
    }
}
