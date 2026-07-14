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
        var subtitle = _contactFilterKey is null
            ? "Личные задачи с датами, важностью и напоминаниями"
            : $"Фильтр: {_contactFilterLabel}";
        SetTitle("Задачи", subtitle);
        var root = new DockPanel();
        var toolbar = new StackPanel();
        var row1 = UiHelpers.ToolbarRow();
        var row2 = UiHelpers.ToolbarRow();
        var list = new StackPanel { Margin = new Thickness(0) };
        WpfTextBox search = null!;
        void AddTaskCards(IEnumerable<TaskItem> taskItems, System.Windows.Controls.Panel target)
        {
            foreach (var task in taskItems)
            {
                target.Children.Add(TaskCard(task));
            }
        }

        void Render()
        {
            list.Children.Clear();
            var query = UiHelpers.EffectiveText(search);
            var today = DateTime.Today;
            var tasks = FilteredTasks(_showTaskArchive)
                .Where(t => string.IsNullOrWhiteSpace(query) ||
                            t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            t.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (tasks.Count == 0)
            {
                list.Children.Add(UiHelpers.EmptyState("Задач нет", _showTaskArchive ? "Архив пуст." : "Создайте задачу или измените фильтр.", "Новая задача", () => AddTask()));
                return;
            }

            if (IsTableView(_viewScope))
            {
                list.Children.Add(TaskTableHeader());
                foreach (var task in tasks
                             .OrderBy(t => TaskGroupSortKey(t, today))
                             .ThenByDescending(t => t.StartAt))
                {
                    list.Children.Add(TaskCard(task));
                }

                return;
            }

            var tileView = IsTileView(_viewScope);

            if (_showTaskArchive)
            {
                if (tileView)
                {
                    var wrap = new WrapPanel { Margin = new Thickness(0) };
                    AddTaskCards(tasks.OrderByDescending(t => t.CreatedAt), wrap);
                    list.Children.Add(wrap);
                }
                else
                {
                    AddTaskCards(tasks.OrderByDescending(t => t.CreatedAt), list);
                }
            }
            else
            {
                foreach (var group in tasks.GroupBy(t => TaskGroupKey(t, today)).OrderBy(g => g.Key switch
                {
                    "В работе" => 0,
                    "На паузе" => 1,
                    "Просрочено" => 2,
                    "Сегодня" => 3,
                    "Завтра" => 4,
                    "Без даты" => 5,
                    _ => 6
                }))
                {
                    list.Children.Add(TaskGroupHeader(group.Key, stretch: tileView));
                    var ordered = group.OrderByDescending(t => t.CreatedAt);
                    if (tileView)
                    {
                        var wrap = new WrapPanel { Margin = new Thickness(0) };
                        AddTaskCards(ordered, wrap);
                        list.Children.Add(wrap);
                    }
                    else
                    {
                        AddTaskCards(ordered, list);
                    }
                }
            }
        }

        search = SearchBox("Поиск задач", Render);
        row1.Children.Add(search);
        row1.Children.Add(ActionButton("Новая задача", () => AddTask()));
        if (_contactFilterKey is not null)
        {
            row1.Children.Add(ActionButton("Сбросить контакт", () =>
            {
                _contactFilterKey = null;
                _contactFilterLabel = null;
                ShowTasks();
            }, false));
        }
        row1.Children.Add(ActionButton(_showTaskArchive ? "Активные" : "Архив", () =>
        {
            _showTaskArchive = !_showTaskArchive;
            ShowTasks();
        }, false));

        var includeProjectTasks = new System.Windows.Controls.CheckBox
        {
            Content = "Задачи проектов",
            IsChecked = _includeProjectTasks,
            Margin = new Thickness(8, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (WpfBrush)FindResource("TextBrush")
        };
        includeProjectTasks.Checked += (_, _) => UpdateIncludeProjectTasks();
        includeProjectTasks.Unchecked += (_, _) => UpdateIncludeProjectTasks();
        row1.Children.Add(includeProjectTasks);

        void UpdateIncludeProjectTasks()
        {
            _includeProjectTasks = includeProjectTasks.IsChecked == true;
            Render();
        }

        var projectFilter = UiHelpers.CreateToolbarComboBox(180);
        projectFilter.Items.Add("(все проекты)");
        foreach (var p in _projects.Projects.OrderByDescending(p => p.CreatedAt)) projectFilter.Items.Add(p);
        projectFilter.SelectedIndex = _taskProjectFilter is null ? 0 : Math.Max(0, _projects.Projects.OrderByDescending(p => p.CreatedAt).ToList().FindIndex(p => p.Id == _taskProjectFilter) + 1);
        projectFilter.SelectionChanged += (_, _) =>
        {
            _taskProjectFilter = projectFilter.SelectedIndex <= 0
                ? null
                : _projects.Projects.OrderByDescending(p => p.CreatedAt).ElementAtOrDefault(projectFilter.SelectedIndex - 1)?.Id;
            Render();
        };
        row2.Children.Add(projectFilter);
        foreach (var imp in new[] { "Все", "Green", "Yellow", "Red" })
        {
            row2.Children.Add(FilterButton(imp == "Green" ? "Низкая" : imp == "Yellow" ? "Средняя" : imp == "Red" ? "Срочная" : "Все",
                _taskImportanceFilter.Equals(imp, StringComparison.OrdinalIgnoreCase),
                () => { _taskImportanceFilter = imp; ShowTasks(); }));
        }
        row2.Children.Add(ToolbarGap());
        AddViewModeButtons(row2, ShowTasks);
        toolbar.Children.Add(row1);
        toolbar.Children.Add(row2);
        var toolbarShell = new Border { Style = (Style)FindResource("SectionToolbar"), Child = toolbar };
        DockPanel.SetDock(toolbarShell, Dock.Top);
        root.Children.Add(toolbarShell);
        Render();
        root.Children.Add(list);
        ContentHost.Content = root;
    }
    private void AddTask(bool forceCommon = false)
    {
        var contextProject = GetCreationContextProject(forceCommon);
        var source = contextProject is not null
            ? new TaskItem { WorkspaceId = contextProject.Id }
            : null;
        var win = new TaskEditorWindow(_projects.Projects, source) { Owner = this };
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
        WindowPlacementService.PlaceOnPrimary(win);
        win.Show();
    }
    private void EditTask(TaskItem task)
    {
        if (EditorWindowHelper.TryActivate(task.Id)) return;

        var win = new TaskEditorWindow(_projects.Projects, task) { Owner = this };
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
        WindowPlacementService.PlaceOnPrimary(win);
        win.Show();
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
        if (_viewScope == "project-detail" && _selectedProject is not null)
        {
            ShowProjectDetail(_selectedProject, "tasks");
            return;
        }

        if (_currentViewKey == "tasks")
        {
            ShowTasks();
            return;
        }

        if (_currentViewKey == "home")
        {
            ShowHome();
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
        if (_viewScope == "project-detail" && _selectedProject is not null)
        {
            ShowProjectDetail(_selectedProject);
            return;
        }
        ShowTasks();
    }
    private void RefreshAfterTaskChange(TaskItem task)
    {
        var project = task.WorkspaceId is { } id ? _projects.Projects.FirstOrDefault(p => p.Id == id) : null;
        if (project is not null && _viewScope == "project-detail")
        {
            ShowProjectDetail(project);
            return;
        }

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

    private void PauseRunningTaskIfProjectChanged(ProjectProfile? project)
    {
        var running = _tasks.Tasks.FirstOrDefault(IsTaskRunning);
        if (running is null || running.WorkspaceId is null)
        {
            return;
        }

        if (project is null || running.WorkspaceId != project.Id)
        {
            PauseTask(running);
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
    private static int TaskGroupSortKey(TaskItem task, DateTime today) => TaskGroupKey(task, today) switch
    {
        "В работе" => 0,
        "На паузе" => 1,
        "Просрочено" => 2,
        "Сегодня" => 3,
        "Завтра" => 4,
        "Без даты" => 5,
        _ => 6
    };

    private FrameworkElement TaskCard(TaskItem task)
    {
        if (IsTableView(_viewScope))
        {
            return TaskCompactRow(task);
        }

        if (IsListView(_viewScope))
        {
            return ListRow(task.Title, () => EditTask(task), ImportanceBrush(task.Importance),
                FavoriteIconButton(task.IsPinned, () => ToggleTaskPinned(task)),
                EditIconButton(() => EditTask(task)));
        }

        var card = Card(task.Title);
        ApplyCardView(card, 360);
        card.MinHeight = 170;
        card.BorderBrush = ImportanceBrush(task.Importance);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                EditTask(task);
            }
        };
        var layout = new Grid();
        var stack = BaseCardStack(task.Title);
        if (IsTaskRunning(task))
        {
            card.BorderBrush = (WpfBrush)FindResource("AccentBrush");
            card.BorderThickness = new Thickness(2);
            stack.Children.Insert(1, UiHelpers.TypeBadge("В работе"));
        }
        else if (IsTaskPaused(task))
        {
            card.BorderBrush = (WpfBrush)FindResource("WarnBrush");
            card.BorderThickness = new Thickness(2);
            stack.Children.Insert(1, UiHelpers.TypeBadge("На паузе"));
        }
        layout.Children.Add(stack);
        layout.Children.Add(EditIconButton(() => EditTask(task)));
        layout.Children.Add(FavoriteIconButton(task.IsPinned, () => ToggleTaskPinned(task), 34));
        layout.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = ImportanceBrush(task.Importance),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 42, 10, 0)
        });
        var meta = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        meta.Children.Add(Muted($"{task.StartAt:dd.MM.yyyy HH:mm}"));
        stack.Children.Add(meta);
        stack.Children.Add(Muted(TaskStatusText(task)));
        if (!string.IsNullOrWhiteSpace(task.ContactName) || !string.IsNullOrWhiteSpace(task.ContactPhone))
        {
            var contactLine = string.IsNullOrWhiteSpace(task.ContactPhone)
                ? task.ContactName
                : $"{task.ContactName} · {task.ContactPhone}";
            stack.Children.Add(Muted(contactLine));
        }
        if (!string.IsNullOrWhiteSpace(task.Description))
        {
        stack.Children.Add(Text(Preview(task.Description, 220), 14, (WpfBrush)FindResource("TextBrush"), new Thickness(0, 10, 0, 10)));
        }
        var buttons = new WrapPanel();
        if (task.IsDone)
        {
            buttons.Children.Add(ActionButton("Вернуть", () => RestoreTask(task), false));
        }
        else if (IsTaskRunning(task))
        {
            buttons.Children.Add(ActionButton("Пауза", () => PauseTask(task), false));
            buttons.Children.Add(ActionButton("Завершить", () => CompleteTaskWithRecurrence(task), false));
        }
        else if (IsTaskPaused(task))
        {
            buttons.Children.Add(ActionButton("Продолжить", () => StartTask(task, openEditor: false)));
            buttons.Children.Add(ActionButton("Завершить", () => CompleteTaskWithRecurrence(task), false));
        }
        else
        {
            buttons.Children.Add(ActionButton("Начать", () => StartTask(task)));
            buttons.Children.Add(ActionButton("Завершить", () => CompleteTaskWithRecurrence(task), false));
        }
        buttons.Children.Add(ActionButton("Удалить", () => DeleteTask(task), false));
        stack.Children.Add(buttons);
        card.Child = layout;
        return card;
    }

    private Border TaskTableHeader() => BuildTableHeader(
        ("Задача", new GridLength(2, GridUnitType.Star)),
        ("Дата", new GridLength(110)),
        ("Статус", new GridLength(100)),
        ("Клиент", new GridLength(130)),
        ("Действия", GridLength.Auto));

    private Border TaskCompactRow(TaskItem task)
    {
        var grid = CreateTableGrid(
            new GridLength(2, GridUnitType.Star),
            new GridLength(110),
            new GridLength(100),
            new GridLength(130),
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

        var contact = string.IsNullOrWhiteSpace(task.ContactName)
            ? task.ContactPhone
            : string.IsNullOrWhiteSpace(task.ContactPhone)
                ? task.ContactName
                : $"{task.ContactName} · {task.ContactPhone}";
        AddCell(grid, 3, Muted(string.IsNullOrWhiteSpace(contact) ? "—" : contact));

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
        actions.Children.Add(CompactIconButton(FavoriteIconButton(task.IsPinned, () => ToggleTaskPinned(task), 28)));
        AddCell(grid, 4, actions);

        return WrapTableRow(grid, () => EditTask(task));
    }
}
