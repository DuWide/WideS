namespace DevCockpit;

public sealed class ConnectionDialog : Form
{
    private readonly TextBox _name = new();
    private readonly ComboBox _type = new();
    private readonly TextBox _address = new();
    private readonly TextBox _login = new();
    private readonly TextBox _password = new();
    private readonly TextBox _tags = new();
    private readonly TextBox _comment = new();
    private readonly ComboBox _workspace = new();
    private readonly IReadOnlyList<ProjectProfile> _workspaces;

    public ConnectionItem Connection { get; }

    public ConnectionDialog(IReadOnlyList<ProjectProfile> workspaces, ConnectionItem? source = null)
    {
        _workspaces = workspaces;
        Connection = source is null
            ? new ConnectionItem()
            : new ConnectionItem
            {
                Id = source.Id,
                Name = source.Name,
                Type = source.Type,
                Address = source.Address,
                Login = source.Login,
                EncryptedPassword = source.EncryptedPassword,
                Comment = source.Comment,
                Tags = source.Tags,
                WorkspaceId = source.WorkspaceId
            };

        Text = source is null ? "Добавить подключение" : "Редактировать подключение";
        Width = 700;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiTheme.App;
        ForeColor = UiTheme.Text;
        Font = UiTheme.BodyFont;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 9, ColumnCount = 2, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        _type.DropDownStyle = ComboBoxStyle.DropDownList;
        _type.Items.AddRange(["AnyDesk", "RDP", "Другое"]);
        _password.UseSystemPasswordChar = true;
        _comment.Multiline = true;
        _comment.Height = 90;
        _workspace.DropDownStyle = ComboBoxStyle.DropDownList;
        _workspace.Items.Add("(без привязки)");
        foreach (var item in _workspaces) _workspace.Items.Add(item);

        AddRow(root, 0, "Название", _name);
        AddRow(root, 1, "Тип", _type);
        AddRow(root, 2, "Адрес / ID", _address);
        AddRow(root, 3, "Логин", _login);
        AddRow(root, 4, "Пароль", _password);
        AddRow(root, 5, "Теги", _tags);
        AddRow(root, 6, "Рабочая папка", _workspace);
        AddRow(root, 7, "Комментарий", _comment);
        for (var i = 0; i < 7; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = UiTheme.Button("Сохранить", ButtonKind.Primary);
        var cancel = UiTheme.Button("Отмена", ButtonKind.Outline);
        ok.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 1, 8);
        LoadConnection();
    }

    private void LoadConnection()
    {
        _name.Text = Connection.Name;
        _type.SelectedItem = string.IsNullOrWhiteSpace(Connection.Type) ? "AnyDesk" : Connection.Type;
        if (_type.SelectedIndex < 0) _type.SelectedIndex = 0;
        _address.Text = Connection.Address;
        _login.Text = Connection.Login;
        _password.Text = SecretService.Unprotect(Connection.EncryptedPassword);
        _tags.Text = Connection.Tags;
        _comment.Text = Connection.Comment;
        _workspace.SelectedIndex = 0;
        if (Connection.WorkspaceId is not null)
        {
            for (var i = 1; i < _workspace.Items.Count; i++)
            {
                if (_workspace.Items[i] is ProjectProfile workspace && workspace.Id == Connection.WorkspaceId)
                {
                    _workspace.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_address.Text))
        {
            MessageBox.Show(this, "Укажите название и адрес/ID.", "WideS");
            return;
        }

        Connection.Name = _name.Text.Trim();
        Connection.Type = _type.SelectedItem?.ToString() ?? "AnyDesk";
        Connection.Address = _address.Text.Trim();
        Connection.Login = _login.Text.Trim();
        Connection.EncryptedPassword = SecretService.Protect(_password.Text);
        Connection.Tags = _tags.Text.Trim();
        Connection.Comment = _comment.Text.Trim();
        Connection.WorkspaceId = _workspace.SelectedItem is ProjectProfile workspace ? workspace.Id : null;
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
