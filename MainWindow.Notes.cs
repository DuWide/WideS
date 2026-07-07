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
    private void ShowNotes()
    {
        EnterView("notes");
        _viewScope = "notes";
        SetTitle("Заметки", "Общие заметки, не привязанные к проектам");
        var root = new DockPanel();
        var top = new WrapPanel { Margin = new Thickness(8) };
        var list = ItemsPanel();
        WpfTextBox search = null!;
        WpfTextBox tagFilter = null!;
        void Render()
        {
            list.Children.Clear();
            var query = UiHelpers.EffectiveText(search);
            var tag = UiHelpers.EffectiveText(tagFilter);
            var notes = _notes.Notes
                         .Where(n => n.WorkspaceId is null)
                         .Where(n => string.IsNullOrWhiteSpace(tag) ||
                                     n.Tags.Contains(tag, StringComparison.OrdinalIgnoreCase))
                         .Where(n => string.IsNullOrWhiteSpace(query) ||
                                     n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                     n.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                     n.Tags.Contains(query, StringComparison.OrdinalIgnoreCase));

            var ordered = IsTableView(_viewScope)
                ? notes.OrderBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase).ToList()
                : notes.OrderByDescending(n => n.CreatedAt).ToList();

            if (IsTableView(_viewScope) && ordered.Count > 0)
            {
                list.Children.Add(NoteTableHeader());
            }

            foreach (var note in ordered)
            {
                list.Children.Add(NoteCard(note));
            }

            if (ordered.Count == 0)
            {
                list.Children.Add(UiHelpers.EmptyState("Заметок нет", "Создайте первую заметку или измените фильтр.", "Новая заметка", () => AddNote()));
            }
        }
        tagFilter = UiHelpers.CreateSearchBox("Тег", Render, minWidth: 160);
        if (!string.IsNullOrWhiteSpace(_noteTagFilter))
        {
            tagFilter.Text = _noteTagFilter;
            tagFilter.Foreground = (WpfBrush)FindResource("TextBrush");
        }
        tagFilter.TextChanged += (_, _) => { _noteTagFilter = UiHelpers.EffectiveText(tagFilter); };
        search = SearchBox("Поиск заметок", Render);
        top.Children.Add(search);
        top.Children.Add(tagFilter);
        top.Children.Add(ActionButton("Новая заметка", () => AddNote()));
        top.Children.Add(ToolbarGap());
        AddViewModeButtons(top, ShowNotes);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        Render();
        root.Children.Add(list);
        ContentHost.Content = root;
    }
    private void ViewNote(NoteItem note)
    {
        if (EditorWindowHelper.TryActivate(note.Id)) return;

        var win = new NoteViewWindow(note) { Owner = this };
        EditorWindowHelper.Register(note.Id, win);
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            _notesStore.Save(_notes);
            AddLog("OK", $"Заметка сохранена: {note.Title}");
            RefreshAfterNoteChange(note);
        };
        WindowPlacementService.PlaceOnSecondary(win);
        win.Show();
    }
    private void ShowProjectNotes(ProjectProfile project)
    {
        _selectedProject = project;
        _viewScope = "project-notes";
        SetTitle($"Заметки: {project.Name}", "Заметки, привязанные только к этому проекту");
        var root = new DockPanel();
        var top = new WrapPanel { Margin = new Thickness(8) };
        var list = ItemsPanel();
        WpfTextBox search = null!;
        void Render()
        {
            list.Children.Clear();
            var query = UiHelpers.EffectiveText(search);
            var notes = _notes.Notes
                         .Where(n => n.WorkspaceId == project.Id)
                         .Where(n => string.IsNullOrWhiteSpace(query) ||
                                     n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                     n.Text.Contains(query, StringComparison.OrdinalIgnoreCase));

            var ordered = IsTableView(_viewScope)
                ? notes.OrderBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase).ToList()
                : notes.OrderByDescending(n => n.CreatedAt).ToList();

            if (IsTableView(_viewScope) && ordered.Count > 0)
            {
                list.Children.Add(NoteTableHeader());
            }

            foreach (var note in ordered)
            {
                list.Children.Add(NoteCard(note));
            }

            if (ordered.Count == 0) list.Children.Add(CardText("Пусто", "Заметки проекта не найдены."));
        }
        search = SearchBox("Поиск заметок проекта", Render);
        top.Children.Add(search);
        top.Children.Add(ActionButton("Новая заметка", () => AddNote()));
        top.Children.Add(ActionButton("Импорт TXT", () => ManualImportTxtNotes(project), false));
        top.Children.Add(ToolbarGap());
        AddViewModeButtons(top, () => ShowProjectNotes(project));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        Render();
        root.Children.Add(list);
        ContentHost.Content = root;
    }
    private void AddNote(bool forceCommon = false)
    {
        var contextProject = GetCreationContextProject(forceCommon);
        var source = contextProject is not null
            ? new NoteItem { WorkspaceId = contextProject.Id, Category = "Проект" }
            : null;
        var win = CreateNoteEditor(source);
        EditorWindowHelper.Register(win.Note.Id, win);
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            win.Note.CreatedAt = DateTime.Now;
            _notes.Notes.Add(win.Note);
            _notesStore.Save(_notes);
            AddLog("OK", $"Заметка добавлена: {win.Note.Title}");
            RefreshAfterNoteChange(win.Note);
        };
        WindowPlacementService.PlaceOnSecondary(win);
        win.Show();
    }
    private void EditNote(NoteItem note)
    {
        if (EditorWindowHelper.TryActivate(note.Id)) return;

        var win = CreateNoteEditor(note);
        EditorWindowHelper.Register(note.Id, win);
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            var index = _notes.Notes.FindIndex(n => n.Id == note.Id);
            if (index >= 0) _notes.Notes[index] = win.Note;
            _notesStore.Save(_notes);
            AddLog("OK", $"Заметка изменена: {win.Note.Title}");
            RefreshAfterNoteChange(win.Note);
        };
        WindowPlacementService.PlaceOnSecondary(win);
        win.Show();
    }
    private NoteEditorWindow CreateNoteEditor(NoteItem? source)
    {
        var win = new NoteEditorWindow(_projects.Projects, source, SaveDetectedConnectionsFromNote) { Owner = this };
        return win;
    }

    private int SaveDetectedConnectionsFromNote(NoteItem note, IReadOnlyList<ConnectionItem> detected)
    {
        var created = 0;
        foreach (var item in detected)
        {
            item.WorkspaceId = note.WorkspaceId;
            if (_connections.Connections.Any(c =>
                    c.Address.Equals(item.Address, StringComparison.OrdinalIgnoreCase) &&
                    c.Type.Equals(item.Type, StringComparison.OrdinalIgnoreCase) &&
                    c.WorkspaceId == item.WorkspaceId))
            {
                continue;
            }

            _connections.Connections.Add(item);
            created++;
        }

        if (created > 0)
        {
            _connectionsStore.Save(_connections);
            AddLog("OK", $"Из заметки создано подключений: {created}");
        }

        return created;
    }

    private void ScanNoteConnections(NoteItem note)
    {
        var detected = NoteConnectionDetector.Detect(note.Text, note.Title);
        if (detected.Count == 0)
        {
            WpfMessageBox.Show(this, "В заметке не найдено подключений для создания.", "WideS");
            return;
        }

        var preview = string.Join("\n", detected.Select(item =>
            $"• {item.Name} [{item.Type}] {item.Address}" +
            (string.IsNullOrWhiteSpace(item.Login) ? "" : $" / {item.Login}")));
        if (WpfMessageBox.Show(this,
                $"Найдено подключений: {detected.Count}\n\n{preview}\n\nСоздать?",
                "WideS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var created = SaveDetectedConnectionsFromNote(note, detected);
        WpfMessageBox.Show(this,
            created > 0 ? $"Создано подключений: {created}." : "Новых подключений не создано — такие уже есть.",
            "WideS");
    }

    private void DeleteNote(NoteItem note)
    {
        if (WpfMessageBox.Show(this, $"Удалить заметку \"{note.Title}\" только из WideS?\nИсходный TXT-файл на диске не будет удалён.", "WideS", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        EditorWindowHelper.CloseRegistered(note.Id);
        _notes.Notes.Remove(note);
        _notesStore.Save(_notes);
        AddLog("WARN", $"Заметка удалена: {note.Title}");
        RefreshAfterNoteChange(note);
    }
    private void DeleteNoteFromDisk(NoteItem note)
    {
        if (string.IsNullOrWhiteSpace(note.SourcePath) || !File.Exists(note.SourcePath))
        {
            WpfMessageBox.Show(this, "У этой заметки нет сохраненного пути к TXT-файлу на диске.", "WideS");
            return;
        }

        if (WpfMessageBox.Show(this,
                $"Удалить заметку \"{note.Title}\" из WideS и файл с диска?\n\n{note.SourcePath}",
                "WideS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        File.Delete(note.SourcePath);
        EditorWindowHelper.CloseRegistered(note.Id);
        _notes.Notes.Remove(note);
        _notesStore.Save(_notes);
        AddLog("WARN", $"Заметка и файл удалены: {note.Title}");
        RefreshAfterNoteChange(note);
    }
    private void RefreshAfterNoteChange(NoteItem note)
    {
        var project = note.WorkspaceId is { } id ? _projects.Projects.FirstOrDefault(p => p.Id == id) : null;
        if (project is not null)
        {
            if (_viewScope == "project-notes")
            {
                ShowProjectNotes(project);
                return;
            }
            ShowProjectDetail(project);
            return;
        }

        ShowNotes();
    }
    private void ManualImportTxtNotes(ProjectProfile project)
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Выберите TXT для заметок проекта",
            Filter = "TXT и LOG (*.txt;*.log)|*.txt;*.log|Все файлы (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = Directory.Exists(project.ProjectFolder)
                ? project.ProjectFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var imported = 0;
        foreach (var file in dialog.FileNames)
        {
            if (!File.Exists(file)) continue;
            var info = new FileInfo(file);
            if (_notes.Notes.Any(n => n.WorkspaceId == project.Id && n.Title.Equals(info.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string text;
            try
            {
                text = info.Length > 2 * 1024 * 1024
                    ? $"Файл больше 2 МБ и не импортирован полностью.\r\nПуть: {file}"
                    : File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                text = $"Не удалось прочитать файл: {ex.Message}\r\nПуть: {file}";
            }

            var relative = Directory.Exists(project.ProjectFolder)
                ? ProjectScanner.Relative(project.ProjectFolder, file)
                : info.Name;
            _notes.Notes.Add(new NoteItem
            {
                Title = info.Name,
                Category = ProjectScanner.IsSuspiciousTextName(info.Name) ? "Доступы" : "Проект",
                Text = $"Источник: {relative}\r\nПроект: {project.Name}\r\n\r\n{text}",
                SourcePath = file,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                WorkspaceId = project.Id,
                IsImportant = ProjectScanner.IsSuspiciousTextName(info.Name)
            });
            imported++;
        }

        _notesStore.Save(_notes);
        AddLog("OK", $"TXT добавлены в заметки проекта: {imported}");
        ShowProjectDetail(project);
    }
    private FrameworkElement NoteCard(NoteItem note)
    {
        if (IsTableView(_viewScope))
        {
            return NoteCompactRow(note);
        }

        if (IsListView(_viewScope))
        {
            return ListRow(note.IsImportant ? "! " + note.Title : note.Title, () => ViewNote(note), note.IsImportant ? (WpfBrush)FindResource("WarnBrush") : null,
                FavoriteIconButton(note.IsPinned, () => ToggleNotePinned(note)),
                EditIconButton(() => EditNote(note)));
        }

        var card = Card(note.IsImportant ? "! " + note.Title : note.Title);
        ApplyCardView(card);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                ViewNote(note);
            }
        };
        var layout = new Grid();
        var stack = BaseCardStack(note.IsImportant ? "! " + note.Title : note.Title);
        layout.Children.Add(stack);
        layout.Children.Add(EditIconButton(() => EditNote(note)));
        layout.Children.Add(FavoriteIconButton(note.IsPinned, () => ToggleNotePinned(note), 34));
        stack.Children.Add(Muted($"{note.Category} · {note.UpdatedAt:yyyy-MM-dd HH:mm}"));
        stack.Children.Add(Text(Preview(note.Text, 230), 14, WpfBrushes.WhiteSmoke, new Thickness(0, 12, 0, 12)));
        var buttons = new WrapPanel();
        buttons.Children.Add(ActionButton("Подключения", () => ScanNoteConnections(note), false));
        buttons.Children.Add(ActionButton("Копировать", () => Copy(note.Text, "Текст заметки скопирован."), false));
        buttons.Children.Add(ActionButton("Удалить", () => DeleteNote(note), false));
        buttons.Children.Add(ActionButton("Удалить с диска", () => DeleteNoteFromDisk(note), false));
        stack.Children.Add(buttons);
        card.Child = layout;
        return card;
    }

    private Border NoteTableHeader() => BuildTableHeader(
        ("Заголовок", new GridLength(2, GridUnitType.Star)),
        ("Категория", new GridLength(100)),
        ("Обновлено", new GridLength(120)),
        ("Действия", GridLength.Auto));

    private Border NoteCompactRow(NoteItem note)
    {
        var grid = CreateTableGrid(
            new GridLength(2, GridUnitType.Star),
            new GridLength(100),
            new GridLength(120),
            GridLength.Auto);

        var title = note.IsImportant ? "! " + note.Title : note.Title;
        var name = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = title
        };
        AddCell(grid, 0, name);
        AddCell(grid, 1, Muted(note.Category));
        AddCell(grid, 2, Muted(note.UpdatedAt.ToString("dd.MM.yyyy HH:mm")));

        var actions = CompactRowActions(
            CompactActionButton("Подк.", () => ScanNoteConnections(note)),
            CompactIconButton(EditIconButton(() => EditNote(note))),
            CompactIconButton(FavoriteIconButton(note.IsPinned, () => ToggleNotePinned(note), 28)));
        AddCell(grid, 3, actions);

        return WrapTableRow(grid, () => ViewNote(note));
    }
}
