using System.Windows;

namespace DevCockpit;

public partial class ConnectionEditorWindow : Window
{
    private readonly IReadOnlyList<ProjectProfile> _projects;
    public bool Saved { get; private set; }
    public ConnectionItem Connection { get; private set; }

    public ConnectionEditorWindow(IReadOnlyList<ProjectProfile> projects, ConnectionItem? source = null)
    {
        InitializeComponent();
        _projects = projects;
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
                IsPinned = source.IsPinned,
                WorkspaceId = source.WorkspaceId
            };
        LoadData();
    }

    private void LoadData()
    {
        TypeBox.ItemsSource = new[] { "AnyDesk", "RDP", "Другое" };
        TypeBox.SelectedItem = string.IsNullOrWhiteSpace(Connection.Type) ? "AnyDesk" : Connection.Type;
        WorkspaceBox.Items.Add("(без привязки)");
        foreach (var item in _projects) WorkspaceBox.Items.Add(item);
        WorkspaceBox.SelectedIndex = 0;
        if (Connection.WorkspaceId is not null)
        {
            for (var i = 1; i < WorkspaceBox.Items.Count; i++)
            {
                if (WorkspaceBox.Items[i] is ProjectProfile project && project.Id == Connection.WorkspaceId)
                {
                    WorkspaceBox.SelectedIndex = i;
                    break;
                }
            }
        }
        NameBox.Text = Connection.Name;
        AddressBox.Text = Connection.Address;
        LoginBox.Text = Connection.Login;
        PasswordBox.Text = SecretService.Unprotect(Connection.EncryptedPassword);
        CommentBox.Text = Connection.Comment;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            System.Windows.MessageBox.Show(this, "Укажите название и адрес/ID.", "WideS");
            return;
        }

        Connection.Name = NameBox.Text.Trim();
        Connection.Type = TypeBox.SelectedItem?.ToString() ?? "AnyDesk";
        Connection.Address = AddressBox.Text.Trim();
        Connection.Login = LoginBox.Text.Trim();
        Connection.EncryptedPassword = SecretService.Protect(PasswordBox.Text);
        Connection.Comment = CommentBox.Text.Trim();
        Connection.WorkspaceId = WorkspaceBox.SelectedItem is ProjectProfile project ? project.Id : null;
        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
