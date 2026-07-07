namespace DevCockpit;

public sealed class NoteDialog : Form
{
    private readonly TextBox _title = new();
    private readonly ComboBox _category = new();
    private readonly TextBox _tags = new();
    private readonly TextBox _text = new();
    private readonly CheckBox _important = new();
    private readonly ComboBox _workspace = new();
    private readonly IReadOnlyList<ProjectProfile> _workspaces;

    public NoteItem Note { get; }

    public NoteDialog(IReadOnlyList<ProjectProfile> workspaces, NoteItem? source = null)
    {
        _workspaces = workspaces;
        Note = source is null
            ? new NoteItem()
            : new NoteItem
            {
                Id = source.Id,
                Title = source.Title,
                Category = source.Category,
                Tags = source.Tags,
                Text = source.Text,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                IsImportant = source.IsImportant,
                WorkspaceId = source.WorkspaceId
            };

        Text = source is null ? "Добавить заметку" : "Редактировать заметку";
        Width = 760;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiTheme.App;
        ForeColor = UiTheme.Text;
        Font = UiTheme.BodyFont;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 2, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        AddRow(root, 0, "Заголовок", _title);
        _category.DropDownStyle = ComboBoxStyle.DropDownList;
        _category.Items.AddRange(["Общее", "Доступы", "Ошибки", "Решения", "Команды", "HTTP", "1С", "Временное"]);
        AddRow(root, 1, "Категория", _category);
        AddRow(root, 2, "Теги", _tags);
        _workspace.DropDownStyle = ComboBoxStyle.DropDownList;
        _workspace.Items.Add("(без привязки)");
        foreach (var item in _workspaces)
        {
            _workspace.Items.Add(item);
        }
        AddRow(root, 3, "Рабочая папка", _workspace);
        _important.Text = "Важная";
        _important.ForeColor = UiTheme.Text;
        root.Controls.Add(new Label(), 0, 4);
        root.Controls.Add(_important, 1, 4);

        _text.Multiline = true;
        _text.ScrollBars = ScrollBars.Vertical;
        UiTheme.StyleTextBox(_text);
        root.Controls.Add(new Label { Text = "Текст", ForeColor = UiTheme.Muted, Dock = DockStyle.Fill }, 0, 5);
        root.Controls.Add(_text, 1, 5);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = UiTheme.Button("Сохранить", ButtonKind.Primary);
        var cancel = UiTheme.Button("Отмена", ButtonKind.Outline);
        ok.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 1, 6);
        LoadNote();
    }

    private void LoadNote()
    {
        _title.Text = Note.Title;
        _category.SelectedItem = string.IsNullOrWhiteSpace(Note.Category) ? "Общее" : Note.Category;
        if (_category.SelectedIndex < 0) _category.SelectedIndex = 0;
        _tags.Text = Note.Tags;
        _text.Text = Note.Text;
        _important.Checked = Note.IsImportant;
        _workspace.SelectedIndex = 0;
        if (Note.WorkspaceId is not null)
        {
            for (var i = 1; i < _workspace.Items.Count; i++)
            {
                if (_workspace.Items[i] is ProjectProfile workspace && workspace.Id == Note.WorkspaceId)
                {
                    _workspace.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            MessageBox.Show(this, "Укажите заголовок заметки.", "WideS");
            return;
        }

        Note.Title = _title.Text.Trim();
        Note.Category = _category.SelectedItem?.ToString() ?? "Общее";
        Note.Tags = _tags.Text.Trim();
        Note.Text = _text.Text;
        Note.IsImportant = _important.Checked;
        Note.WorkspaceId = _workspace.SelectedItem is ProjectProfile workspace ? workspace.Id : null;
        Note.UpdatedAt = DateTime.Now;
        DialogResult = DialogResult.OK;
    }

    private static void AddRow(TableLayoutPanel root, int row, string label, Control control)
    {
        root.Controls.Add(new Label { Text = label, ForeColor = UiTheme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill;
        if (control is TextBox box) UiTheme.StyleTextBox(box);
        root.Controls.Add(control, 1, row);
    }
}
