namespace DevCockpit;

public sealed class ProjectDialog : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _folder = new();
    private readonly TextBox _editor = new();
    private readonly TextBox _workspace = new();
    private readonly TextBox _releases = new();
    private readonly TextBox _tags = new();
    private readonly TextBox _comment = new();

    public ProjectProfile Project { get; }

    public ProjectDialog(ProjectProfile? source = null)
    {
        Project = source is null
            ? new ProjectProfile()
            : new ProjectProfile
            {
                Id = source.Id,
                Name = source.Name,
                ProjectFolder = source.ProjectFolder,
                EditorPath = source.EditorPath,
                WorkspacePath = source.WorkspacePath,
                ReleasesFolder = source.ReleasesFolder,
                Comment = source.Comment,
                Tags = source.Tags
            };

        Text = source is null ? "Добавить проект" : "Редактировать проект";
        Width = 720;
        Height = 440;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 8,
            Padding = new Padding(12),
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        AddRow(layout, 0, "Название", _name);
        AddPathRow(layout, 1, "Папка проекта", _folder, true);
        AddPathRow(layout, 2, "Cursor/редактор", _editor, false);
        AddPathRow(layout, 3, ".code-workspace", _workspace, false);
        AddPathRow(layout, 4, "Папка релизов", _releases, true);
        AddRow(layout, 5, "Теги", _tags);
        AddRow(layout, 6, "Комментарий", _comment);
        _comment.Multiline = true;
        _comment.Height = 80;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = DarkButton("OK");
        var cancel = DarkButton("Отмена");
        ok.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 1, 7);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        LoadProject();
    }

    private void LoadProject()
    {
        _name.Text = Project.Name;
        _folder.Text = Project.ProjectFolder;
        _editor.Text = Project.EditorPath;
        _workspace.Text = Project.WorkspacePath;
        _releases.Text = Project.ReleasesFolder;
        _tags.Text = Project.Tags;
        _comment.Text = Project.Comment;
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_folder.Text))
        {
            MessageBox.Show(this, "Укажите название и папку проекта.", "WideS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Project.Name = _name.Text.Trim();
        Project.ProjectFolder = _folder.Text.Trim();
        Project.EditorPath = _editor.Text.Trim();
        Project.WorkspacePath = _workspace.Text.Trim();
        Project.ReleasesFolder = _releases.Text.Trim();
        Project.Tags = _tags.Text.Trim();
        Project.Comment = _comment.Text.Trim();
        DialogResult = DialogResult.OK;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, TextBox box)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        StyleTextBox(box);
        layout.Controls.Add(box, 1, row);
        layout.SetColumnSpan(box, 2);
    }

    private static void AddPathRow(TableLayoutPanel layout, int row, string label, TextBox box, bool folder)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        StyleTextBox(box);
        layout.Controls.Add(box, 1, row);
        var browse = DarkButton("Выбрать");
        browse.Click += (_, _) =>
        {
            if (folder)
            {
                using var dialog = new FolderBrowserDialog();
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    box.Text = dialog.SelectedPath;
                }
            }
            else
            {
                using var dialog = new OpenFileDialog();
                dialog.Filter = "Все файлы|*.*";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    box.Text = dialog.FileName;
                }
            }
        };
        layout.Controls.Add(browse, 2, row);
    }

    internal static Button DarkButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Height = 34,
        BackColor = Color.FromArgb(55, 55, 58),
        ForeColor = Color.WhiteSmoke,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4)
    };

    internal static void StyleTextBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BackColor = Color.FromArgb(42, 42, 45);
        box.ForeColor = Color.WhiteSmoke;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Margin = new Padding(4, 6, 4, 6);
    }
}
