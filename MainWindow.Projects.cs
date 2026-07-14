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
    private void ShowProjects()
    {
        EnterView("projects");
        _viewScope = "projects";
        SetTitle("Проекты", "Проекты, workspace, TXT-заметки, backup, context и папка дня");
        var root = SectionWithActions(actions =>
        {
            actions.Children.Add(ActionButton("Добавить проект", AddProject));
            foreach (var st in new[] { "Active", "Paused", "Archive", "All" })
            {
                var label = st switch { "Active" => "Активные", "Paused" => "Пауза", "Archive" => "Архив", _ => "Все" };
                actions.Children.Add(FilterButton(label, _projectStatusFilter == st, () => { _projectStatusFilter = st; ShowProjects(); }));
            }
            actions.Children.Add(ToolbarGap());
            AddViewModeButtons(actions, ShowProjects);
        }, out var panel);
        var projects = _projects.Projects
                     .Where(p => _projectStatusFilter == "All" || p.Status.Equals(_projectStatusFilter, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(p => p.CreatedAt)
                     .ToList();
        if (IsTableView(_viewScope) && projects.Count > 0 && panel is StackPanel)
        {
            panel.Children.Add(ProjectTableHeader());
        }

        foreach (var project in projects)
        {
            panel.Children.Add(ProjectCard(project));
        }
        if (panel.Children.Count == 0) panel.Children.Add(UiHelpers.EmptyState("Проектов нет", "Добавьте первый проект.", "Добавить проект", () => AddProject()));
        ContentHost.Content = root;
    }
    private void ShowProjectDetail(ProjectProfile project, string? tab = null)
    {
        if (tab is not null) _projectDetailTab = tab;
        EnterView($"project:{project.Id}");
        _viewScope = "project-detail";
        _selectedProject = project;
        TouchProjectOpened(project);
        SetTitle(project.Name, "Заметки, задачи, подключения и инструменты проекта");
        var root = new DockPanel();

        var top = new WrapPanel();
        top.Children.Add(ActionButton("Рабочая область", () => StartFocusProject(project)));
        top.Children.Add(ActionButton("Открыть папку", OpenSelectedFolder));
        top.Children.Add(ActionButton("Cursor", OpenWorkspace, false));
        top.Children.Add(ActionButton("Удалить проект", () => DeleteProject(project), false));
        top.Children.Add(ActionButton("Удалить с диска", () => DeleteProjectFromDisk(project), false));
        top.Children.Add(ToolbarGap());
        AddViewModeButtons(top, () => ShowProjectDetail(project));
        var topShell = new Border { Style = (Style)FindResource("SectionToolbar"), Child = top };
        DockPanel.SetDock(topShell, Dock.Top);
        root.Children.Add(topShell);

        var pathRow = new Border
        {
            Background = (WpfBrush)FindResource("PanelBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(8, 0, 8, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        pathRow.Child = Text(project.ProjectFolder, 13, (WpfBrush)FindResource("AccentBrush"), new Thickness());
        pathRow.MouseLeftButtonUp += (_, _) =>
        {
            if (Directory.Exists(project.ProjectFolder))
            {
                var explorer = ShellHelper.OpenPath(project.ProjectFolder);
                WindowPlacementService.MoveProcessToPrimaryAsync(explorer, "explorer");
            }
        };
        DockPanel.SetDock(pathRow, Dock.Top);
        root.Children.Add(pathRow);

        var tabs = new WrapPanel { Margin = new Thickness(8, 0, 8, 8) };
        void AddTab(string label, string key)
        {
            tabs.Children.Add(FilterButton(label, _projectDetailTab == key, () =>
            {
                _projectDetailTab = key;
                ShowProjectDetail(project);
            }));
        }
        AddTab("Заметки", "notes");
        AddTab("Задачи", "tasks");
        AddTab("Подключения", "connections");
        AddTab("Инструменты", "tools");
        DockPanel.SetDock(tabs, Dock.Top);
        root.Children.Add(tabs);

        var contentHost = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        contentHost.Content = _projectDetailTab switch
        {
            "tasks" => BuildProjectTasksTab(project),
            "connections" => BuildProjectConnectionsTab(project),
            "tools" => BuildProjectToolsTab(project),
            _ => BuildProjectNotesTab(project)
        };
        root.Children.Add(contentHost);
        ContentHost.Content = root;
    }
    private Border ProjectNotesSection(ProjectProfile project, IReadOnlyList<NoteItem> notes)
    {
        var section = new Border
        {
            Background = (WpfBrush)FindResource("PanelBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(6),
            Width = IsListView(_viewScope) ? 760 : 700,
            MinHeight = 150
        };
        section.Cursor = System.Windows.Input.Cursors.Hand;
        section.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                ShowProjectNotes(project);
            }
        };

        var stack = BaseCardStack("Заметки проекта");
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(ActionButton("Новая заметка", () => AddNote()));
        actions.Children.Add(ActionButton("Импорт TXT", () => ManualImportTxtNotes(project), false));
        stack.Children.Add(actions);
        if (notes.Count == 0)
        {
            stack.Children.Add(Muted("Нет заметок."));
        }
        else
        {
            foreach (var note in notes.Take(3))
            {
                var row = ProjectEntityRow(
                    note.IsImportant ? "! " + note.Title : note.Title,
                    () => ViewNote(note),
                    note.IsImportant ? (WpfBrush)FindResource("WarnBrush") : null,
                    () => EditNote(note),
                    () => DeleteNote(note));
                row.Width = IsListView(_viewScope) ? 700 : 640;
                row.Margin = new Thickness(0, 3, 0, 3);
                stack.Children.Add(row);
            }
            if (notes.Count > 3)
            {
                stack.Children.Add(Muted($"Еще заметок: {notes.Count - 3}. Нажмите блок, чтобы открыть все."));
            }
        }

        section.Child = stack;
        return section;
    }
    private Border ProjectConnectionsSection(ProjectProfile project, IReadOnlyList<ConnectionItem> connections)
    {
        var section = new Border
        {
            Background = (WpfBrush)FindResource("PanelBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(6),
            Width = IsListView(_viewScope) ? 760 : 700,
            MinHeight = 150
        };

        var stack = BaseCardStack("Подключения проекта");
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(ActionButton("Новое подключение", AddConnection));
        stack.Children.Add(actions);
        if (connections.Count == 0)
        {
            stack.Children.Add(Muted("Нет подключений."));
        }
        else
        {
            foreach (var connection in connections)
            {
                var row = ListRow($"{connection.Type}: {connection.Name}", () => Connect(connection));
                row.Width = IsListView(_viewScope) ? 700 : 640;
                row.Margin = new Thickness(0, 4, 0, 4);
                stack.Children.Add(row);
            }
        }

        section.Child = stack;
        return section;
    }
    private Border ProjectTasksSection(ProjectProfile project, IReadOnlyList<TaskItem> tasks)
    {
        var section = new Border
        {
            Background = (WpfBrush)FindResource("PanelBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(6),
            Width = IsListView(_viewScope) ? 760 : 340,
            MinHeight = 150
        };

        var stack = BaseCardStack("Задачи проекта");
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(ActionButton("Новая задача", () => AddTask()));
        stack.Children.Add(actions);
        if (tasks.Count == 0)
        {
            stack.Children.Add(Muted("Нет задач."));
        }
        else
        {
            foreach (var task in tasks.Take(5))
            {
                var row = ProjectEntityRow(
                    $"{task.StartAt:dd.MM}: {task.Title}",
                    () => EditTask(task),
                    ImportanceBrush(task.Importance),
                    () => EditTask(task),
                    () => DeleteTask(task));
                row.Width = IsListView(_viewScope) ? 700 : 280;
                row.Margin = new Thickness(0, 3, 0, 3);
                stack.Children.Add(row);
            }
        }

        section.Child = stack;
        return section;
    }
    private Border ProjectToolsSection(ProjectProfile project)
    {
        var section = Card("Инструменты проекта");
        section.Width = IsListView(_viewScope) ? 760 : 700;
        section.MinHeight = 120;
        var stack = BaseCardStack("Инструменты проекта");
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Backup", BackupSelected, false));
        actions.Children.Add(ActionButton("Скопировать в AI", CopyForAiSelected, false));
        actions.Children.Add(ActionButton("Отчет дня", BuildDailyReport, false));
        actions.Children.Add(ActionButton("Шаблон", () => ApplyProjectTemplate(project), false));
        stack.Children.Add(actions);
        section.Child = stack;
        return section;
    }
    private void StartFocusProject(ProjectProfile project)
    {
        PauseRunningTaskIfProjectChanged(project);
        _selectedProject = project;
        _focusProject = project;
        _focusStartedAt = DateTime.Now;
        TouchProjectOpened(project);
        AppPaths.EnsureTodayWorkDay(project);

        if (Directory.Exists(project.ProjectFolder))
        {
            var explorer = ShellHelper.OpenPath(project.ProjectFolder);
            WindowPlacementService.MoveProcessToPrimaryAsync(explorer, "explorer");
        }

        if (ProjectHasWorkspace(project))
        {
            try
            {
                AddLog("OK", ShellHelper.OpenWithEditor(project, FindWorkspaceFile(project)));
            }
            catch (Exception ex)
            {
                AddLog("ERR", $"Cursor: {ex.Message}");
            }
        }
        else
        {
            AddLog("OK", "Cursor не настроен — открыты папка и заметки проекта.");
        }

        AddLog("OK", $"Рабочая область: {project.Name}");
        ShowProjectDetail(project, "notes");
    }
    private void ToggleProjectPinned(ProjectProfile project)
    {
        project.IsPinned = !project.IsPinned;
        _projectStore.Save(_projects);
        AddLog("OK", project.IsPinned ? $"Проект закреплен: {project.Name}" : $"Проект откреплен: {project.Name}");
        RefreshAfterPinnedChange();
    }
    private void ApplyProjectTemplate(ProjectProfile project)
    {
        var template = _templates.Templates.FirstOrDefault();
        if (template is null)
        {
            AddLog("ERR", "Шаблон проекта не найден.");
            return;
        }

        foreach (var folder in template.Folders)
        {
            Directory.CreateDirectory(Path.Combine(project.ProjectFolder, folder));
        }

        foreach (var title in template.NoteTitles.Where(t => !_notes.Notes.Any(n => n.WorkspaceId == project.Id && n.Title == t)))
        {
            _notes.Notes.Add(new NoteItem
            {
                Title = title,
                Category = "Проект",
                WorkspaceId = project.Id,
                Text = $"Проект: {project.Name}\r\n\r\n",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }

        foreach (var title in template.TaskTitles.Where(t => !_tasks.Tasks.Any(x => x.WorkspaceId == project.Id && x.Title == t)))
        {
            _tasks.Tasks.Add(new TaskItem
            {
                Title = title,
                Description = $"Шаблонная задача проекта {project.Name}",
                WorkspaceId = project.Id,
                StartAt = DateTime.Now,
                EndAt = DateTime.Now.AddHours(1),
                ReminderAt = DateTime.Now
            });
        }

        _notesStore.Save(_notes);
        _tasksStore.Save(_tasks);
        AddLog("OK", $"Шаблон применен к проекту: {project.Name}");
        ShowProjectDetail(project);
    }
    private void AddProject()
    {
        var win = new WorkspaceEditorWindow() { Owner = this };
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            win.Workspace.CreatedAt = win.Workspace.CreatedAt.Year < 2000 ? DateTime.Now : win.Workspace.CreatedAt;
            _projects.Projects.Add(win.Workspace);
            _projectStore.Save(_projects);
            _selectedProject = win.Workspace;
            AddLog("OK", $"Проект добавлен: {win.Workspace.Name}");
            RefreshProjectSwitcher();
            ShowProjects();
        };
        win.Show();
    }
    private void EditProject(ProjectProfile project)
    {
        var win = new WorkspaceEditorWindow(project) { Owner = this };
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            var index = _projects.Projects.FindIndex(w => w.Id == project.Id);
            if (index >= 0) _projects.Projects[index] = win.Workspace;
            _projectStore.Save(_projects);
            _selectedProject = win.Workspace;
            AddLog("OK", $"Проект изменён: {win.Workspace.Name}");
            RefreshProjectSwitcher();
            ShowProjects();
        };
        win.Show();
    }
    private void DeleteProject(ProjectProfile project)
    {
        if (WpfMessageBox.Show(this,
                $"Удалить проект \"{project.Name}\" только из WideS?\nФайлы и папки на диске не будут удалены.",
                "WideS",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        RemoveProjectFromWideS(project);
        RefreshProjectSwitcher();
        AddLog("WARN", $"Проект удалён из WideS без удаления файлов: {project.Name}");
        ShowProjects();
    }
    private void DeleteProjectFromDisk(ProjectProfile project)
    {
        if (string.IsNullOrWhiteSpace(project.ProjectFolder) || !Directory.Exists(project.ProjectFolder))
        {
            WpfMessageBox.Show(this, "Папка проекта на диске не найдена.", "WideS");
            return;
        }

        if (WpfMessageBox.Show(this,
                $"Удалить проект \"{project.Name}\" из WideS и полностью удалить папку с диска?\n\n{project.ProjectFolder}",
                "WideS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        if (WpfMessageBox.Show(this,
                "Это действие нельзя отменить. Точно удалить папку проекта со всеми файлами?",
                "WideS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        Directory.Delete(project.ProjectFolder, true);
        RemoveProjectFromWideS(project);
        RefreshProjectSwitcher();
        AddLog("WARN", $"Проект и папка удалены с диска: {project.Name}");
        ShowProjects();
    }
    private void RemoveProjectFromWideS(ProjectProfile project)
    {
        _projects.Projects.Remove(project);
        foreach (var note in _notes.Notes.Where(n => n.WorkspaceId == project.Id)) note.WorkspaceId = null;
        foreach (var connection in _connections.Connections.Where(c => c.WorkspaceId == project.Id)) connection.WorkspaceId = null;
        foreach (var task in _tasks.Tasks.Where(t => t.WorkspaceId == project.Id)) task.WorkspaceId = null;
        if (_selectedProject?.Id == project.Id) _selectedProject = _projects.Projects.FirstOrDefault();
        if (_focusProject?.Id == project.Id) _focusProject = null;

        _projectStore.Save(_projects);
        _notesStore.Save(_notes);
        _connectionsStore.Save(_connections);
        _tasksStore.Save(_tasks);
        _openTabs.Remove($"project:{project.Id}");
    }
    private void OpenSelectedFolder()
    {
        var project = RequireProject();
        if (project is null) return;
        TouchProjectOpened(project);
        RefreshProjectSwitcher();
        var explorer = ShellHelper.OpenPath(project.ProjectFolder);
        WindowPlacementService.MoveProcessToPrimaryAsync(explorer, "explorer");
        AddLog("OK", $"Открыта папка: {project.ProjectFolder}");
    }
    private void OpenWorkspace(ProjectProfile project)
    {
        _selectedProject = project;
        try
        {
            AddLog("OK", ShellHelper.OpenWithEditor(project, FindWorkspaceFile(project)));
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Cursor/workspace: {ex.Message}");
        }
    }
    private void OpenWorkday()
    {
        var project = RequireProject();
        if (project is null) return;
        var day = AppPaths.EnsureTodayWorkDay(project);
        var explorer = ShellHelper.OpenPath(day);
        WindowPlacementService.MoveProcessToPrimaryAsync(explorer, "explorer");
        AddLog("OK", $"Открыта папка дня: {day}");
    }
    private FrameworkElement ProjectCard(ProjectProfile project)
    {
        if (IsTableView(_viewScope))
        {
            return ProjectCompactRow(project);
        }

        if (IsListView(_viewScope))
        {
            var row = ListRow(project.Name, () => ShowProjectDetail(project), project.IsPinned ? (WpfBrush)FindResource("WarnBrush") : null,
                FavoriteIconButton(project.IsPinned, () => ToggleProjectPinned(project)),
                EditIconButton(() => EditProject(project)));
            row.ToolTip = $"Статус: {UiHelpers.ProjectStatusDisplay(project.Status)}";
            return row;
        }

        var card = Card(project.Name);
        ApplyCardView(card, 450);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                ShowProjectDetail(project);
            }
        };
        var layout = new Grid();
        var stack = BaseCardStack(project.Name);
        layout.Children.Add(stack);
        layout.Children.Add(EditIconButton(() => EditProject(project)));
        layout.Children.Add(FavoriteIconButton(project.IsPinned, () => ToggleProjectPinned(project), 34));
        layout.Children.Add(ProjectMoreButton(project));
        stack.Children.Add(Muted(project.ProjectFolder));
        stack.Children.Add(UiHelpers.TypeBadge(UiHelpers.ProjectStatusDisplay(project.Status)));
        stack.Children.Add(Muted($"Открывал: {UiHelpers.RelativeDays(project.LastOpenedAt)}"));
        if (!string.IsNullOrWhiteSpace(project.Comment)) stack.Children.Add(Text(project.Comment, 14, (WpfBrush)FindResource("TextBrush"), new Thickness(0, 12, 0, 8)));
        var projectNotes = _notes.Notes.Count(n => n.WorkspaceId == project.Id);
        stack.Children.Add(Muted($"Заметок проекта: {projectNotes}"));
        var primary = new WrapPanel { Margin = new Thickness(0, 12, 0, 4) };
        primary.Children.Add(ActionButton("Рабочая область", () => StartFocusProject(project)));
        primary.Children.Add(ActionButton("Открыть", () => { _selectedProject = project; OpenSelectedFolder(); }));
        primary.Children.Add(ActionButton("Cursor", () => { _selectedProject = project; OpenWorkspace(); }, false));
        stack.Children.Add(primary);

        card.Child = layout;
        return card;
    }

    private WpfButton ProjectMoreButton(ProjectProfile project)
    {
        var menu = new ContextMenu();
        void AddItem(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        AddItem(project.IsPinned ? "Убрать из избранного" : "Добавить в избранное", () => ToggleProjectPinned(project));
        menu.Items.Add(new Separator());
        if (!project.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)) AddItem("Сделать активным", () => SetProjectStatus(project, "Active"));
        if (!project.Status.Equals("Paused", StringComparison.OrdinalIgnoreCase)) AddItem("Поставить на паузу", () => SetProjectStatus(project, "Paused"));
        if (!project.Status.Equals("Archive", StringComparison.OrdinalIgnoreCase)) AddItem("Переместить в архив", () => SetProjectStatus(project, "Archive"));
        menu.Items.Add(new Separator());
        AddItem("Создать Backup", () => { _selectedProject = project; BackupSelected(); });
        AddItem("Скопировать в AI", () => { _selectedProject = project; CopyForAiSelected(); });
        AddItem("Открыть папку дня", () => { _selectedProject = project; OpenWorkday(); });
        AddItem("Применить шаблон", () => ApplyProjectTemplate(project));
        menu.Items.Add(new Separator());
        AddItem("Удалить из WideS", () => DeleteProject(project));
        AddItem("Удалить с диска", () => DeleteProjectFromDisk(project));

        WpfButton? button = null;
        button = IconButton("more", () =>
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }, "Другие действия", 28);
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Thickness(0, 0, 68, 0);
        return button;
    }

    private Border ProjectTableHeader() => BuildTableHeader(
        ("Проект", new GridLength(1.8, GridUnitType.Star)),
        ("Статус", new GridLength(90)),
        ("Открывал", new GridLength(120)),
        ("Действия", GridLength.Auto));

    private Border ProjectCompactRow(ProjectProfile project)
    {
        var grid = CreateTableGrid(
            new GridLength(1.8, GridUnitType.Star),
            new GridLength(90),
            new GridLength(120),
            GridLength.Auto);

        var name = new TextBlock
        {
            Text = project.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = project.Name
        };
        AddCell(grid, 0, name);

        var status = UiHelpers.TypeBadge(UiHelpers.ProjectStatusDisplay(project.Status));
        status.Margin = new Thickness(0);
        AddCell(grid, 1, status);

        AddCell(grid, 2, Muted(UiHelpers.RelativeDays(project.LastOpenedAt)));

        var actions = CompactRowActions(
            CompactActionButton("Открыть", () => ShowProjectDetail(project)),
            CompactIconButton(FavoriteIconButton(project.IsPinned, () => ToggleProjectPinned(project), 28)),
            CompactIconButton(EditIconButton(() => EditProject(project))));
        AddCell(grid, 3, actions);

        return WrapTableRow(grid, () => ShowProjectDetail(project));
    }

    private FrameworkElement BuildProjectNotesTab(ProjectProfile project)
    {
        var root = new StackPanel { Margin = new Thickness(8) };
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        WpfTextBox search = null!;
        var list = ItemsPanel();
        void Render()
        {
            list.Children.Clear();
            var query = UiHelpers.EffectiveText(search);
            var notes = _notes.Notes
                         .Where(n => n.WorkspaceId == project.Id)
                         .Where(n => string.IsNullOrWhiteSpace(query) ||
                                     n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                     n.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(n => n.UpdatedAt)
                         .ToList();

            if (notes.Count == 0)
            {
                list.Children.Add(UiHelpers.EmptyState("Нет заметок", "Создайте заметку для проекта.", "Новая заметка", () => AddNote()));
                return;
            }

            if (IsTableView(_viewScope) && list is StackPanel)
            {
                list.Children.Add(NoteTableHeader());
            }

            foreach (var note in notes)
            {
                list.Children.Add(NoteCard(note));
            }
        }
        search = SearchBox("Поиск заметок", Render);
        top.Children.Add(search);
        top.Children.Add(ActionButton("Новая заметка", () => AddNote()));
        top.Children.Add(ActionButton("Импорт TXT", () => ManualImportTxtNotes(project), false));
        root.Children.Add(top);
        Render();
        root.Children.Add(list);
        return root;
    }
    private FrameworkElement BuildProjectTasksTab(ProjectProfile project)
    {
        var root = new StackPanel { Margin = new Thickness(8) };
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var list = ItemsPanel();
        void Render()
        {
            list.Children.Clear();
            var tasks = _tasks.Tasks
                .Where(t => t.WorkspaceId == project.Id)
                .Where(t => _projectTasksArchive ? t.IsDone : !t.IsDone)
                .OrderByDescending(t => t.StartAt)
                .ToList();

            if (tasks.Count == 0)
            {
                list.Children.Add(UiHelpers.EmptyState("Нет задач", _projectTasksArchive ? "Архив пуст." : "Создайте задачу.", "Новая задача", () => AddTask()));
                return;
            }

            if (IsTableView(_viewScope) && list is StackPanel)
            {
                list.Children.Add(TaskTableHeader());
            }

            foreach (var task in tasks)
            {
                list.Children.Add(TaskCard(task));
            }
        }
        top.Children.Add(ActionButton("Новая задача", () => AddTask()));
        top.Children.Add(ActionButton(_projectTasksArchive ? "Активные" : "Архив", () =>
        {
            _projectTasksArchive = !_projectTasksArchive;
            ShowProjectDetail(project, "tasks");
        }, false));
        root.Children.Add(top);
        Render();
        root.Children.Add(list);
        return root;
    }
    private FrameworkElement BuildProjectConnectionsTab(ProjectProfile project)
    {
        var root = new DockPanel();
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Controls.Panel list = ItemsPanel();
        WpfTextBox search = null!;

        void Render()
        {
            list.Children.Clear();
            var query = UiHelpers.EffectiveText(search);
            var items = _connections.Connections
                .Where(c => c.WorkspaceId == project.Id)
                .Where(c => _connectionTypeFilter == "Все" || c.Type.Equals(_connectionTypeFilter, StringComparison.OrdinalIgnoreCase))
                .Where(c => string.IsNullOrWhiteSpace(query) ||
                            c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            c.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            c.Login.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (items.Count == 0)
            {
                list.Children.Add(UiHelpers.EmptyState("Нет подключений", "Добавьте RDP или AnyDesk.", "Новое подключение", AddConnection));
                return;
            }

            if (IsTableView(_viewScope) && items.Count > 0)
            {
                list.Children.Add(ConnectionTableHeader());
            }

            foreach (var connection in items)
            {
                list.Children.Add(ConnectionCard(connection));
            }

            if (items.Count > 0 && IsTableView(_viewScope))
            {
                list.Children.Add(Muted($"{items.Count} подключений · сортировка по названию"));
            }
        }

        search = SearchBox("Поиск подключения", Render);
        toolbar.Children.Add(search);
        foreach (var value in new[] { "Все", "AnyDesk", "RDP" })
        {
            toolbar.Children.Add(FilterButton(value, _connectionTypeFilter == value, () =>
            {
                _connectionTypeFilter = value;
                ShowProjectDetail(project, "connections");
            }));
        }
        toolbar.Children.Add(ActionButton("Новое подключение", AddConnection));
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(list);
        Render();
        return root;
    }
    private FrameworkElement BuildProjectToolsTab(ProjectProfile project)
    {
        var root = new StackPanel { Margin = new Thickness(8) };
        var card = Card("Инструменты");
        card.Width = 760;
        var stack = BaseCardStack("Инструменты проекта");
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Backup", BackupSelected, false));
        actions.Children.Add(ActionButton("Скопировать в AI", CopyForAiSelected, false));
        actions.Children.Add(ActionButton("Отчет дня", BuildDailyReport, false));
        actions.Children.Add(ActionButton("Шаблон", () => ApplyProjectTemplate(project), false));
        actions.Children.Add(ActionButton("DropZone", OpenDropZoneFolder, false));
        stack.Children.Add(actions);
        card.Child = stack;
        root.Children.Add(card);
        return root;
    }
}
