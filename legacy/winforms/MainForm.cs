namespace DevCockpit;

public sealed class MainForm : Form
{
    private readonly ProjectStore _workspaceStore = new();
    private readonly JsonFileStore<NotesStoreData> _notesStore = new(AppPaths.NotesJson);
    private readonly JsonFileStore<ConnectionsStoreData> _connectionsStore = new(AppPaths.ConnectionsJson);
    private readonly JsonFileStore<AppSettingsData> _settingsStore = new(AppPaths.SettingsJson);

    private ProjectStoreData _workspaces = new();
    private NotesStoreData _notes = new();
    private ConnectionsStoreData _connections = new();
    private AppSettingsData _settings = new();
    private ScanResult _scan = new();

    private readonly Panel _content = new();
    private readonly TextBox _log = new();
    private readonly ListBox _workspaceList = new();
    private readonly TextBox _workspaceSearch = new();
    private readonly TextBox _noteSearch = new();
    private readonly ComboBox _noteCategory = new();
    private readonly ListBox _notesList = new();
    private readonly TextBox _notePreview = new();
    private readonly TextBox _connectionSearch = new();
    private readonly ListBox _connectionsList = new();
    private readonly TextBox _connectionPreview = new();
    private readonly DataGridView _txtGrid = new();
    private readonly ListBox _workspaceFiles = new();
    private readonly ListBox _backupList = new();
    private readonly Label _dropLabel = new();
    private readonly TextBox _anyDeskPath = new();

    private string _section = "Главная";

    private ProjectProfile? SelectedWorkspace => _workspaceList.SelectedItem as ProjectProfile;
    private NoteItem? SelectedNote => _notesList.SelectedItem as NoteItem;
    private ConnectionItem? SelectedConnection => _connectionsList.SelectedItem as ConnectionItem;

    public MainForm()
    {
        Text = "WideS";
        Width = 1260;
        Height = 800;
        MinimumSize = new Size(1080, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.App;
        ForeColor = UiTheme.Text;
        Font = UiTheme.BodyFont;

        LoadData();
        BuildUi();
        ShowSection("Главная");
        AddLog("OK", "WideS запущен.");
    }

    private void LoadData()
    {
        _workspaces = _workspaceStore.Load();
        _notes = _notesStore.Load();
        _connections = _connectionsStore.Load();
        _settings = _settingsStore.Load();
    }

    private void BuildUi()
    {
        var root = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 250,
            FixedPanel = FixedPanel.Panel1,
            BackColor = UiTheme.App
        };
        Controls.Add(root);
        BuildNavigation(root.Panel1);

        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(16), BackColor = UiTheme.App };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.Panel2.Controls.Add(workspace);

        workspace.Controls.Add(BuildHeader(), 0, 0);
        _content.Dock = DockStyle.Fill;
        _content.BackColor = UiTheme.App;
        workspace.Controls.Add(_content, 0, 1);

        var logCard = Card("События", "OK");
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        UiTheme.StyleTextBox(_log);
        logCard.Controls.Add(_log);
        workspace.Controls.Add(logCard, 0, 2);
    }

    private void BuildNavigation(Control host)
    {
        host.BackColor = UiTheme.Sidebar;
        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(14), BackColor = UiTheme.Sidebar };
        nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        host.Controls.Add(nav);

        var title = UiTheme.Label("WideS", UiTheme.TitleFont);
        title.TextAlign = ContentAlignment.MiddleLeft;
        nav.Controls.Add(title, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var section in new[] { "Главная", "Проекты", "Заметки", "Подключения", "Контекст AI", "Backup", "DropZone", "Настройки" })
        {
            var button = UiTheme.Button(section, ButtonKind.Outline);
            button.Width = 210;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Click += (_, _) => ShowSection(section);
            buttons.Controls.Add(button);
        }
        nav.Controls.Add(buttons, 0, 1);

        var hint = UiTheme.Label("Локально · без API · без облака", UiTheme.SmallFont, UiTheme.Muted);
        hint.TextAlign = ContentAlignment.MiddleLeft;
        nav.Controls.Add(hint, 0, 2);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.App };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 690));

        var title = new Label
        {
            Text = "Личный рабочий центр разработчика",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Text,
            Font = UiTheme.TitleFont,
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(title, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
        AddButton(actions, "DropZone", ButtonKind.Outline, ShowFloatingDropZone);
        AddButton(actions, "Context", ButtonKind.Secondary, BuildContextForSelected);
        AddButton(actions, "Backup", ButtonKind.Secondary, BackupSelectedWorkspace);
        AddButton(actions, "Открыть папку", ButtonKind.Secondary, OpenSelectedWorkspaceFolder);
        AddButton(actions, "Добавить подключение", ButtonKind.Outline, AddConnection);
        AddButton(actions, "Добавить заметку", ButtonKind.Primary, AddNote);
        header.Controls.Add(actions, 1, 0);
        return header;
    }

    private void ShowSection(string section)
    {
        _section = section;
        _content.Controls.Clear();
        Control page = section switch
        {
            "Проекты" => BuildWorkspacesPage(),
            "Заметки" => BuildNotesPage(),
            "Подключения" => BuildConnectionsPage(),
            "Контекст AI" => BuildContextPage(),
            "Backup" => BuildBackupPage(),
            "DropZone" => BuildDropZonePage(),
            "Настройки" => BuildSettingsPage(),
            _ => BuildDashboardPage()
        };
        _content.Controls.Add(page);
    }

    private Control BuildDashboardPage()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(4) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        grid.Controls.Add(CardWithText("Быстрые подключения", "RDP", DashboardConnections()), 0, 0);
        grid.Controls.Add(CardWithText("Последние заметки", "N", DashboardNotes()), 1, 0);
        grid.Controls.Add(CardWithText("Проекты", "DIR", DashboardWorkspaces()), 2, 0);
        grid.Controls.Add(CardWithText("Последние backup", "ZIP", DashboardBackups()), 0, 1);

        var actions = Card("Быстрые действия", "GO");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddButton(panel, "Добавить заметку", ButtonKind.Primary, AddNote);
        AddButton(panel, "Добавить подключение", ButtonKind.Secondary, AddConnection);
        AddButton(panel, "Рабочая папка", ButtonKind.Secondary, AddWorkspace);
        AddButton(panel, "Папка дня", ButtonKind.Outline, EnsureWorkDayForSelected);
        AddButton(panel, "Context", ButtonKind.Outline, BuildContextForSelected);
        AddButton(panel, "Backup", ButtonKind.Outline, BackupSelectedWorkspace);
        actions.Controls.Add(panel);
        grid.Controls.Add(actions, 1, 1);
        grid.SetColumnSpan(actions, 2);
        return grid;
    }

    private Control BuildWorkspacesPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = Card("Проекты", "DIR");
        var leftLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        _workspaceSearch.PlaceholderText = "Поиск папки...";
        UiTheme.StyleTextBox(_workspaceSearch);
        _workspaceSearch.Dock = DockStyle.Fill;
        _workspaceSearch.TextChanged -= WorkspaceSearchChanged;
        _workspaceSearch.TextChanged += WorkspaceSearchChanged;
        leftLayout.Controls.Add(_workspaceSearch, 0, 0);
        _workspaceList.Dock = DockStyle.Fill;
        _workspaceList.BackColor = UiTheme.Card;
        _workspaceList.ForeColor = UiTheme.Text;
        _workspaceList.BorderStyle = BorderStyle.None;
        _workspaceList.SelectedIndexChanged -= WorkspaceSelectedChanged;
        _workspaceList.SelectedIndexChanged += WorkspaceSelectedChanged;
        leftLayout.Controls.Add(_workspaceList, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddButton(buttons, "Добавить", ButtonKind.Primary, AddWorkspace);
        AddButton(buttons, "Изменить", ButtonKind.Secondary, EditWorkspace);
        AddButton(buttons, "Удалить", ButtonKind.Danger, DeleteWorkspace);
        leftLayout.Controls.Add(buttons, 0, 2);
        left.Controls.Add(leftLayout);
        root.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actionCard = Card("Действия с рабочей папкой", "RUN");
        var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddButton(actionPanel, "Открыть папку", ButtonKind.Primary, OpenSelectedWorkspaceFolder);
        AddButton(actionPanel, "Открыть в Cursor", ButtonKind.Secondary, OpenSelectedWorkspaceInCursor);
        AddButton(actionPanel, "Папка дня", ButtonKind.Secondary, EnsureWorkDayForSelected);
        AddButton(actionPanel, "Backup", ButtonKind.Outline, BackupSelectedWorkspace);
        AddButton(actionPanel, "Context", ButtonKind.Outline, BuildContextForSelected);
        AddButton(actionPanel, "Пересканировать", ButtonKind.Outline, ScanSelectedWorkspace);
        actionCard.Controls.Add(actionPanel);
        right.Controls.Add(actionCard, 0, 0);

        var scanGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        scanGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        scanGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        UiTheme.StyleGrid(_txtGrid);
        scanGrid.Controls.Add(CardWithControl("TXT / заметки в папке", "TXT", _txtGrid), 0, 0);
        scanGrid.Controls.Add(CardWithControl("Workspace / XML / результаты", "INFO", _workspaceFiles), 1, 0);
        right.Controls.Add(scanGrid, 0, 1);
        root.Controls.Add(right, 1, 0);

        RefreshWorkspaceList();
        return root;
    }

    private Control BuildNotesPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = Card("Заметки", "N");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        _noteSearch.PlaceholderText = "Поиск по заметкам...";
        UiTheme.StyleTextBox(_noteSearch);
        _noteSearch.TextChanged -= NotesFilterChanged;
        _noteSearch.TextChanged += NotesFilterChanged;
        layout.Controls.Add(_noteSearch, 0, 0);
        _noteCategory.DropDownStyle = ComboBoxStyle.DropDownList;
        _noteCategory.Items.Clear();
        _noteCategory.Items.AddRange(["Все", "Общее", "Доступы", "Ошибки", "Решения", "Команды", "HTTP", "1С", "Временное"]);
        if (_noteCategory.SelectedIndex < 0) _noteCategory.SelectedIndex = 0;
        _noteCategory.SelectedIndexChanged -= NotesFilterChanged;
        _noteCategory.SelectedIndexChanged += NotesFilterChanged;
        layout.Controls.Add(_noteCategory, 0, 1);
        _notesList.Dock = DockStyle.Fill;
        _notesList.BackColor = UiTheme.Card;
        _notesList.ForeColor = UiTheme.Text;
        _notesList.BorderStyle = BorderStyle.None;
        _notesList.SelectedIndexChanged -= NoteSelectedChanged;
        _notesList.SelectedIndexChanged += NoteSelectedChanged;
        _notesList.DoubleClick -= OpenNoteWindow;
        _notesList.DoubleClick += OpenNoteWindow;
        layout.Controls.Add(_notesList, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddButton(buttons, "Добавить", ButtonKind.Primary, AddNote);
        AddButton(buttons, "Изменить", ButtonKind.Secondary, EditNote);
        AddButton(buttons, "Удалить", ButtonKind.Danger, DeleteNote);
        AddButton(buttons, "Копировать", ButtonKind.Outline, CopyNoteText);
        layout.Controls.Add(buttons, 0, 3);
        left.Controls.Add(layout);
        root.Controls.Add(left, 0, 0);

        var previewCard = Card("Текст заметки", "TEXT");
        _notePreview.Dock = DockStyle.Fill;
        _notePreview.Multiline = true;
        _notePreview.ReadOnly = true;
        _notePreview.ScrollBars = ScrollBars.Vertical;
        UiTheme.StyleTextBox(_notePreview);
        previewCard.Controls.Add(_notePreview);
        root.Controls.Add(previewCard, 1, 0);
        RefreshNotesList();
        return root;
    }

    private Control BuildConnectionsPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = Card("Подключения", "RDP");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        _connectionSearch.PlaceholderText = "Поиск подключения...";
        UiTheme.StyleTextBox(_connectionSearch);
        _connectionSearch.TextChanged -= ConnectionsFilterChanged;
        _connectionSearch.TextChanged += ConnectionsFilterChanged;
        layout.Controls.Add(_connectionSearch, 0, 0);
        _connectionsList.Dock = DockStyle.Fill;
        _connectionsList.BackColor = UiTheme.Card;
        _connectionsList.ForeColor = UiTheme.Text;
        _connectionsList.BorderStyle = BorderStyle.None;
        _connectionsList.SelectedIndexChanged -= ConnectionSelectedChanged;
        _connectionsList.SelectedIndexChanged += ConnectionSelectedChanged;
        layout.Controls.Add(_connectionsList, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddButton(buttons, "Добавить", ButtonKind.Primary, AddConnection);
        AddButton(buttons, "Изменить", ButtonKind.Secondary, EditConnection);
        AddButton(buttons, "Удалить", ButtonKind.Danger, DeleteConnection);
        AddButton(buttons, "Подключиться", ButtonKind.Primary, ConnectSelected);
        AddButton(buttons, "ID", ButtonKind.Outline, CopyConnectionAddress);
        AddButton(buttons, "Логин", ButtonKind.Outline, CopyConnectionLogin);
        AddButton(buttons, "Пароль", ButtonKind.Outline, CopyConnectionPassword);
        layout.Controls.Add(buttons, 0, 2);
        left.Controls.Add(layout);
        root.Controls.Add(left, 0, 0);

        var preview = Card("Карточка подключения", "LOCK");
        _connectionPreview.Dock = DockStyle.Fill;
        _connectionPreview.Multiline = true;
        _connectionPreview.ReadOnly = true;
        _connectionPreview.ScrollBars = ScrollBars.Vertical;
        UiTheme.StyleTextBox(_connectionPreview);
        preview.Controls.Add(_connectionPreview);
        root.Controls.Add(preview, 1, 0);
        RefreshConnectionsList();
        return root;
    }

    private Control BuildContextPage()
    {
        return BuildToolPage("Контекст AI", "Выберите проект в разделе Проекты и соберите context.txt. Подозрительные txt не выбираются автоматически.", BuildContextForSelected);
    }

    private Control BuildBackupPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = Card("Backup выбранной рабочей папки", "ZIP");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddButton(panel, "Сделать backup", ButtonKind.Primary, BackupSelectedWorkspace);
        AddButton(panel, "Открыть папку backup", ButtonKind.Secondary, OpenBackupFolderForSelected);
        AddButton(panel, "Обновить список", ButtonKind.Outline, RefreshBackupList);
        actions.Controls.Add(panel);
        root.Controls.Add(actions, 0, 0);
        _backupList.Dock = DockStyle.Fill;
        _backupList.BackColor = UiTheme.Card;
        _backupList.ForeColor = UiTheme.Text;
        _backupList.BorderStyle = BorderStyle.None;
        _backupList.DoubleClick -= OpenSelectedBackup;
        _backupList.DoubleClick += OpenSelectedBackup;
        root.Controls.Add(CardWithControl("Последние backup", "B", _backupList), 0, 1);
        RefreshBackupList();
        return root;
    }

    private Control BuildDropZonePage()
    {
        var card = Card("DropZone", "DROP");
        _dropLabel.Dock = DockStyle.Fill;
        _dropLabel.Text = "Перетащите сюда файлы для выбранной рабочей папки\r\nScreens · Errors · Archives · Code · Temp · Releases";
        _dropLabel.TextAlign = ContentAlignment.MiddleCenter;
        _dropLabel.BorderStyle = BorderStyle.FixedSingle;
        _dropLabel.AllowDrop = true;
        _dropLabel.BackColor = Color.FromArgb(31, 43, 50);
        _dropLabel.ForeColor = UiTheme.Text;
        _dropLabel.Font = new Font("Segoe UI Semibold", 13);
        _dropLabel.DragEnter -= DropZoneDragEnter;
        _dropLabel.DragEnter += DropZoneDragEnter;
        _dropLabel.DragLeave += (_, _) => _dropLabel.BackColor = Color.FromArgb(31, 43, 50);
        _dropLabel.DragDrop -= DropZoneDragDrop;
        _dropLabel.DragDrop += DropZoneDragDrop;
        card.Controls.Add(_dropLabel);
        return card;
    }

    private Control BuildSettingsPage()
    {
        var card = Card("Настройки", "SET");
        var root = new TableLayoutPanel { Dock = DockStyle.Top, RowCount = 3, ColumnCount = 3, Height = 140 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.Controls.Add(new Label { Text = "AnyDesk.exe", ForeColor = UiTheme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _anyDeskPath.Text = _settings.AnyDeskPath;
        _anyDeskPath.Dock = DockStyle.Fill;
        UiTheme.StyleTextBox(_anyDeskPath);
        root.Controls.Add(_anyDeskPath, 1, 0);
        var browse = UiTheme.Button("Выбрать", ButtonKind.Outline);
        browse.Click += (_, _) => BrowseAnyDesk();
        root.Controls.Add(browse, 2, 0);
        var save = UiTheme.Button("Сохранить настройки", ButtonKind.Primary);
        save.Click += (_, _) => SaveSettings();
        root.Controls.Add(save, 1, 1);
        card.Controls.Add(root);
        return card;
    }

    private Control BuildToolPage(string title, string description, Action action)
    {
        var card = Card(title, "GO");
        var box = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 90,
            Multiline = true,
            ReadOnly = true,
            Text = description,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Card,
            ForeColor = UiTheme.Muted,
            Font = new Font("Segoe UI", 12)
        };
        var button = UiTheme.Button(title, ButtonKind.Primary);
        button.Dock = DockStyle.Top;
        button.Click += (_, _) => action();
        card.Controls.Add(button);
        card.Controls.Add(box);
        return card;
    }

    private string DashboardConnections()
    {
        if (_connections.Connections.Count == 0) return "Подключения пока не добавлены.";
        return string.Join(Environment.NewLine, _connections.Connections.Take(6).Select(x => $"{x.Type}: {x.Name} · {x.Address}"));
    }

    private string DashboardNotes()
    {
        if (_notes.Notes.Count == 0) return "Заметки пока не добавлены.";
        return string.Join(Environment.NewLine, _notes.Notes.OrderByDescending(x => x.UpdatedAt).Take(6).Select(x => $"{(x.IsImportant ? "! " : "")}{x.Title} · {x.Category}"));
    }

    private string DashboardWorkspaces()
    {
        if (_workspaces.Projects.Count == 0) return "Проекты пока не добавлены.";
        return string.Join(Environment.NewLine, _workspaces.Projects.Take(6).Select(x => $"{x.Name} · {x.ProjectFolder}"));
    }

    private string DashboardBackups()
    {
        var backups = _workspaces.Projects
            .SelectMany(x =>
            {
                var dir = Path.Combine(AppPaths.TodayWorkDay(x), "Backups");
                return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.zip") : [];
            })
            .OrderByDescending(File.GetLastWriteTime)
            .Take(5)
            .ToList();
        return backups.Count == 0 ? "Backup пока не найдены." : string.Join(Environment.NewLine, backups);
    }

    private void RefreshWorkspaceList()
    {
        var selectedId = SelectedWorkspace?.Id;
        var filter = _workspaceSearch.Text.Trim();
        _workspaceList.Items.Clear();
        foreach (var item in _workspaces.Projects.Where(x =>
            string.IsNullOrWhiteSpace(filter) ||
            x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            x.ProjectFolder.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            x.Tags.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            _workspaceList.Items.Add(item);
        }
        RestoreListSelection(_workspaceList, selectedId);
        if (_workspaceList.SelectedIndex < 0 && _workspaceList.Items.Count > 0) _workspaceList.SelectedIndex = 0;
    }

    private void WorkspaceSearchChanged(object? sender, EventArgs e) => RefreshWorkspaceList();
    private void WorkspaceSelectedChanged(object? sender, EventArgs e) => ScanSelectedWorkspace(false);

    private void AddWorkspace()
    {
        using var dialog = new ProjectDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _workspaces.Projects.Add(dialog.Project);
        _workspaceStore.Save(_workspaces);
        RefreshWorkspaceList();
        AddLog("OK", $"Рабочая папка добавлена: {dialog.Project.Name}");
    }

    private void EditWorkspace()
    {
        var workspace = RequireWorkspace();
        if (workspace is null) return;
        using var dialog = new ProjectDialog(workspace);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var index = _workspaces.Projects.FindIndex(x => x.Id == workspace.Id);
        if (index >= 0)
        {
            _workspaces.Projects[index] = dialog.Project;
            _workspaceStore.Save(_workspaces);
            RefreshWorkspaceList();
            AddLog("OK", $"Рабочая папка изменена: {dialog.Project.Name}");
        }
    }

    private void DeleteWorkspace()
    {
        var workspace = RequireWorkspace();
        if (workspace is null) return;
        if (MessageBox.Show(this, $"Удалить проект из списка: {workspace.Name}?", "WideS", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _workspaces.Projects.Remove(workspace);
        _workspaceStore.Save(_workspaces);
        RefreshWorkspaceList();
        AddLog("WARN", $"Рабочая папка удалена из списка: {workspace.Name}");
    }

    private void ScanSelectedWorkspace() => ScanSelectedWorkspace(true);

    private void ScanSelectedWorkspace(bool log)
    {
        var workspace = SelectedWorkspace;
        if (workspace is null) return;
        _scan = ProjectScanner.Scan(workspace);
        _txtGrid.DataSource = _scan.TextFiles.Select(x => new
        {
            Файл = x.IsSuspicious ? "⚠ " + x.Name : x.Name,
            Путь = x.RelativePath,
            Размер = FormatBytes(x.Size),
            Изменен = x.Modified.ToString("yyyy-MM-dd HH:mm"),
            x.FullPath
        }).ToList();
        if (_txtGrid.Columns["FullPath"] is not null) _txtGrid.Columns["FullPath"].Visible = false;

        _workspaceFiles.Items.Clear();
        AddFileGroup("Workspace", _scan.Workspaces);
        AddFileGroup("XML folders", _scan.XmlFolders.Select(x => $"{x.RelativePath} ({x.Count})"));
        AddFileGroup("Results", _scan.ResultFiles);
        if (log) AddLog("OK", $"Рабочая папка пересканирована: {workspace.Name}");
    }

    private void AddFileGroup(string title, IEnumerable<string> items)
    {
        _workspaceFiles.Items.Add($"-- {title} --");
        var any = false;
        foreach (var item in items)
        {
            _workspaceFiles.Items.Add(item);
            any = true;
        }
        if (!any) _workspaceFiles.Items.Add("не найдено");
    }

    private void OpenSelectedWorkspaceFolder()
    {
        var workspace = RequireWorkspace();
        if (workspace is null) return;
        ShellHelper.OpenPath(workspace.ProjectFolder);
        AddLog("OK", $"Открыта папка: {workspace.ProjectFolder}");
    }

    private void OpenSelectedWorkspaceInCursor()
    {
        var workspace = RequireWorkspace();
        if (workspace is null) return;
        var foundWorkspace = _scan.Workspaces.FirstOrDefault();
        var message = ShellHelper.OpenWithEditor(workspace, foundWorkspace is null ? null : Path.Combine(workspace.ProjectFolder, foundWorkspace));
        AddLog(message.StartsWith("Не удалось") ? "WARN" : "OK", message);
    }

    private void EnsureWorkDayForSelected()
    {
        var workspace = RequireWorkspace();
        if (workspace is null) return;
        var day = AppPaths.EnsureTodayWorkDay(workspace);
        ShellHelper.OpenPath(day);
        AddLog("OK", $"Папка дня создана/открыта: {day}");
    }

    private void BackupSelectedWorkspace()
    {
        var workspace = RequireWorkspace();
        if (workspace is null) return;
        try
        {
            var zip = BackupService.CreateBackup(workspace);
            Clipboard.SetText(zip);
            AddLog("OK", $"Backup создан: {zip}");
            RefreshBackupList();
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Ошибка backup: {ex.Message}");
        }
    }

    private void OpenBackupFolderForSelected()
    {
        var workspace = RequireWorkspace();
        if (workspace is null) return;
        ShellHelper.OpenPath(Path.Combine(AppPaths.EnsureTodayWorkDay(workspace), "Backups"));
    }

    private void RefreshBackupList()
    {
        _backupList.Items.Clear();
        var workspace = SelectedWorkspace;
        var backups = workspace is null
            ? _workspaces.Projects.SelectMany(x =>
            {
                var dir = Path.Combine(AppPaths.TodayWorkDay(x), "Backups");
                return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.zip") : [];
            })
            : Directory.Exists(Path.Combine(AppPaths.TodayWorkDay(workspace), "Backups"))
                ? Directory.GetFiles(Path.Combine(AppPaths.TodayWorkDay(workspace), "Backups"), "*.zip")
                : [];
        foreach (var backup in backups.OrderByDescending(File.GetLastWriteTime).Take(30))
        {
            _backupList.Items.Add(backup);
        }
        if (_backupList.Items.Count == 0) _backupList.Items.Add("Backup пока не найдены");
    }

    private void OpenSelectedBackup(object? sender, EventArgs e)
    {
        if (_backupList.SelectedItem is string path && File.Exists(path)) ShellHelper.OpenPath(path);
    }

    private void BuildContextForSelected()
    {
        var workspace = RequireWorkspace();
        if (workspace is null) return;
        using var dialog = new ContextBuilderDialog(workspace);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.CreatedContextPath is not null)
        {
            AddLog("OK", $"context.txt собран: {dialog.CreatedContextPath}");
        }
    }

    private void RefreshNotesList()
    {
        var selectedId = SelectedNote?.Id;
        var query = _noteSearch.Text.Trim();
        var category = _noteCategory.SelectedItem?.ToString() ?? "Все";
        _notesList.Items.Clear();
        foreach (var note in _notes.Notes
            .Where(x => category == "Все" || x.Category == category)
            .Where(x => string.IsNullOrWhiteSpace(query) ||
                        x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.Tags.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.IsImportant)
            .ThenByDescending(x => x.UpdatedAt))
        {
            _notesList.Items.Add(note);
        }
        RestoreListSelection(_notesList, selectedId);
        if (_notesList.SelectedIndex < 0 && _notesList.Items.Count > 0) _notesList.SelectedIndex = 0;
        NoteSelectedChanged(null, EventArgs.Empty);
    }

    private void NotesFilterChanged(object? sender, EventArgs e) => RefreshNotesList();
    private void NoteSelectedChanged(object? sender, EventArgs e)
    {
        var note = SelectedNote;
        _notePreview.Text = note is null
            ? "Выберите заметку."
            : $"{(note.IsImportant ? "ВАЖНАЯ · " : "")}{note.Title}\r\n{note.Category} · {note.Tags}\r\nСоздана: {note.CreatedAt:yyyy-MM-dd HH:mm} · Изменена: {note.UpdatedAt:yyyy-MM-dd HH:mm}\r\n\r\n{note.Text}";
    }

    private void AddNote()
    {
        using var dialog = new NoteDialog(_workspaces.Projects);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _notes.Notes.Add(dialog.Note);
        _notesStore.Save(_notes);
        RefreshNotesList();
        AddLog("OK", $"Заметка добавлена: {dialog.Note.Title}");
    }

    private void EditNote()
    {
        var note = SelectedNote;
        if (note is null) return;
        using var dialog = new NoteDialog(_workspaces.Projects, note);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var index = _notes.Notes.FindIndex(x => x.Id == note.Id);
        if (index >= 0) _notes.Notes[index] = dialog.Note;
        _notesStore.Save(_notes);
        RefreshNotesList();
        AddLog("OK", $"Заметка изменена: {dialog.Note.Title}");
    }

    private void DeleteNote()
    {
        var note = SelectedNote;
        if (note is null) return;
        if (MessageBox.Show(this, $"Удалить заметку: {note.Title}?", "WideS", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _notes.Notes.Remove(note);
        _notesStore.Save(_notes);
        RefreshNotesList();
        AddLog("WARN", $"Заметка удалена: {note.Title}");
    }

    private void CopyNoteText()
    {
        if (SelectedNote is null) return;
        Clipboard.SetText(SelectedNote.Text);
        AddLog("OK", "Текст заметки скопирован.");
    }

    private void OpenNoteWindow(object? sender, EventArgs e)
    {
        if (SelectedNote is null) return;
        MessageBox.Show(this, SelectedNote.Text, SelectedNote.Title);
    }

    private void RefreshConnectionsList()
    {
        var selectedId = SelectedConnection?.Id;
        var query = _connectionSearch.Text.Trim();
        _connectionsList.Items.Clear();
        foreach (var connection in _connections.Connections
            .Where(x => string.IsNullOrWhiteSpace(query) ||
                        x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.Tags.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name))
        {
            _connectionsList.Items.Add(connection);
        }
        RestoreListSelection(_connectionsList, selectedId);
        if (_connectionsList.SelectedIndex < 0 && _connectionsList.Items.Count > 0) _connectionsList.SelectedIndex = 0;
        ConnectionSelectedChanged(null, EventArgs.Empty);
    }

    private void ConnectionsFilterChanged(object? sender, EventArgs e) => RefreshConnectionsList();
    private void ConnectionSelectedChanged(object? sender, EventArgs e)
    {
        var item = SelectedConnection;
        _connectionPreview.Text = item is null
            ? "Выберите подключение."
            : $"{item.Name}\r\nТип: {item.Type}\r\nАдрес/ID: {item.Address}\r\nЛогин: {item.Login}\r\nПароль: {(string.IsNullOrWhiteSpace(item.EncryptedPassword) ? "(не задан)" : "(зашифрован DPAPI)")}\r\nТеги: {item.Tags}\r\n\r\n{item.Comment}";
    }

    private void AddConnection()
    {
        using var dialog = new ConnectionDialog(_workspaces.Projects);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _connections.Connections.Add(dialog.Connection);
        _connectionsStore.Save(_connections);
        RefreshConnectionsList();
        AddLog("OK", $"Подключение добавлено: {dialog.Connection.Name}");
    }

    private void EditConnection()
    {
        var item = SelectedConnection;
        if (item is null) return;
        using var dialog = new ConnectionDialog(_workspaces.Projects, item);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var index = _connections.Connections.FindIndex(x => x.Id == item.Id);
        if (index >= 0) _connections.Connections[index] = dialog.Connection;
        _connectionsStore.Save(_connections);
        RefreshConnectionsList();
        AddLog("OK", $"Подключение изменено: {dialog.Connection.Name}");
    }

    private void DeleteConnection()
    {
        var item = SelectedConnection;
        if (item is null) return;
        if (MessageBox.Show(this, $"Удалить подключение: {item.Name}?", "WideS", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _connections.Connections.Remove(item);
        _connectionsStore.Save(_connections);
        RefreshConnectionsList();
        AddLog("WARN", $"Подключение удалено: {item.Name}");
    }

    private void ConnectSelected()
    {
        if (SelectedConnection is null) return;
        AddLog("OK", ConnectionService.Connect(SelectedConnection, _settings));
    }

    private void CopyConnectionAddress()
    {
        if (SelectedConnection is null) return;
        Clipboard.SetText(SelectedConnection.Address);
        AddLog("OK", "Адрес/ID скопирован.");
    }

    private void CopyConnectionLogin()
    {
        if (SelectedConnection is null) return;
        Clipboard.SetText(SelectedConnection.Login);
        AddLog("OK", "Логин скопирован.");
    }

    private void CopyConnectionPassword()
    {
        if (SelectedConnection is null) return;
        Clipboard.SetText(SecretService.Unprotect(SelectedConnection.EncryptedPassword));
        AddLog("OK", "Пароль скопирован.");
    }

    private void BrowseAnyDesk()
    {
        using var dialog = new OpenFileDialog { Filter = "AnyDesk.exe|AnyDesk.exe|Все файлы|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _anyDeskPath.Text = dialog.FileName;
        }
    }

    private void SaveSettings()
    {
        _settings.AnyDeskPath = _anyDeskPath.Text.Trim();
        _settingsStore.Save(_settings);
        AddLog("OK", "Настройки сохранены.");
    }

    private void ShowFloatingDropZone()
    {
        if (RequireWorkspace() is null) return;
        var zone = new Form
        {
            Text = "WideS DropZone",
            Width = 380,
            Height = 230,
            TopMost = true,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = UiTheme.App,
            ForeColor = UiTheme.Text,
            Font = UiTheme.BodyFont,
            AllowDrop = true
        };
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Перетащите файлы сюда\r\nScreens · Errors · Archives · Code · Temp · Releases",
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(31, 43, 50),
            ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI Semibold", 11)
        };
        label.AllowDrop = true;
        label.DragEnter += DropZoneDragEnter;
        label.DragDrop += DropZoneDragDrop;
        zone.Controls.Add(label);
        zone.DragEnter += DropZoneDragEnter;
        zone.DragDrop += DropZoneDragDrop;
        zone.Show(this);
    }

    private void DropZoneDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            _dropLabel.BackColor = Color.FromArgb(43, 70, 82);
        }
    }

    private void DropZoneDragDrop(object? sender, DragEventArgs e)
    {
        _dropLabel.BackColor = Color.FromArgb(31, 43, 50);
        var workspace = RequireWorkspace();
        if (workspace is null) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;

        var copied = new List<string>();
        try
        {
            var day = AppPaths.EnsureTodayWorkDay(workspace);
            foreach (var file in files.Where(File.Exists))
            {
                var targetDir = Path.Combine(day, DropZoneFolder(Path.GetExtension(file)));
                Directory.CreateDirectory(targetDir);
                var target = UniquePath(Path.Combine(targetDir, Path.GetFileName(file)));
                File.Copy(file, target);
                copied.Add(target);
            }
            _dropLabel.Text = copied.Count == 0 ? "Файлы не скопированы" : "Добавлено:\r\n" + string.Join(Environment.NewLine, copied.Select(Path.GetFileName));
            AddLog("OK", $"DropZone добавила файлов: {copied.Count}");
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Ошибка DropZone: {ex.Message}");
        }
    }

    private static string DropZoneFolder(string extension)
    {
        extension = extension.ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp") return "Screens";
        if (extension is ".txt" or ".log") return "Errors";
        if (extension is ".zip" or ".rar" or ".7z") return "Archives";
        if (extension is ".bsl" or ".cs" or ".py" or ".sql" or ".json" or ".xml" or ".md") return "Code";
        if (extension is ".epf" or ".erf" or ".cfe" or ".cf") return "Releases";
        return "Temp";
    }

    private ProjectProfile? RequireWorkspace()
    {
        var workspace = SelectedWorkspace ?? _workspaces.Projects.FirstOrDefault();
        if (workspace is not null) return workspace;
        MessageBox.Show(this, "Сначала добавьте или выберите проект.", "WideS");
        return null;
    }

    private void RestoreListSelection(ListBox list, Guid? id)
    {
        if (id is null) return;
        for (var i = 0; i < list.Items.Count; i++)
        {
            var itemId = list.Items[i] switch
            {
                ProjectProfile p => p.Id,
                NoteItem n => n.Id,
                ConnectionItem c => c.Id,
                _ => Guid.Empty
            };
            if (itemId == id)
            {
                list.SelectedIndex = i;
                return;
            }
        }
    }

    private void AddLog(string status, string message)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {status}  {message}{Environment.NewLine}");
    }

    private static void AddButton(FlowLayoutPanel panel, string text, ButtonKind kind, Action action)
    {
        var button = UiTheme.Button(text, kind);
        button.Click += (_, _) => action();
        panel.Controls.Add(button);
    }

    private static CardPanel Card(string title, string icon) => new()
    {
        Title = title,
        IconText = icon,
        Dock = DockStyle.Fill
    };

    private static Control CardWithText(string title, string icon, string text)
    {
        var card = Card(title, icon);
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Card,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.BodyFont,
            Text = text
        };
        card.Controls.Add(box);
        return card;
    }

    private static Control CardWithControl(string title, string icon, Control control)
    {
        var card = Card(title, icon);
        control.Dock = DockStyle.Fill;
        if (control is ListBox list)
        {
            list.BackColor = UiTheme.Card;
            list.ForeColor = UiTheme.Text;
            list.BorderStyle = BorderStyle.None;
            list.Font = UiTheme.BodyFont;
        }
        card.Controls.Add(control);
        return card;
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

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / 1024.0 / 1024.0:0.#} MB";
    }
}
