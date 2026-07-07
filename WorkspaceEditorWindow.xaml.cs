using System.Windows;

namespace DevCockpit;

public partial class WorkspaceEditorWindow : Window
{
    public bool Saved { get; private set; }
    public ProjectProfile Workspace { get; private set; }
    private bool _userEditedName;
    private bool _userEditedWorkspace;
    private bool _userEditedComment;
    private bool _suppressAutoFill;

    public WorkspaceEditorWindow(ProjectProfile? source = null)
    {
        InitializeComponent();
        Workspace = source is null
            ? new ProjectProfile()
            : new ProjectProfile
            {
                Id = source.Id,
                Name = source.Name,
                ProjectFolder = source.ProjectFolder,
                WorkspacePath = source.WorkspacePath,
                EditorPath = source.EditorPath,
                ReleasesFolder = source.ReleasesFolder,
                Comment = source.Comment,
                IsPinned = source.IsPinned,
                Tags = source.Tags,
                Status = source.Status,
                LastOpenedAt = source.LastOpenedAt,
                CreatedAt = source.CreatedAt
            };
        NameBox.Text = Workspace.Name;
        StatusBox.SelectedIndex = Workspace.Status switch
        {
            "Paused" => 1,
            "Archive" => 2,
            _ => 0
        };
        FolderBox.Text = Workspace.ProjectFolder;
        WorkspaceBox.Text = Workspace.WorkspacePath;
        CommentBox.Text = Workspace.Comment;

        NameBox.TextChanged += (_, _) => { if (!_suppressAutoFill) _userEditedName = true; };
        WorkspaceBox.TextChanged += (_, _) => { if (!_suppressAutoFill) _userEditedWorkspace = true; };
        CommentBox.TextChanged += (_, _) => { if (!_suppressAutoFill) _userEditedComment = true; };
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderBox.Text = dialog.SelectedPath;
            ApplyFolderDetection(dialog.SelectedPath);
        }
    }

    private void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        ApplyFolderDetection(FolderBox.Text.Trim());
    }

    private void FolderBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // ручной ввод пути не сканируем автоматически
    }

    private void ApplyFolderDetection(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            System.Windows.MessageBox.Show(this, "Укажите существующую папку проекта.", "WideS");
            return;
        }

        var detected = ProjectFolderDetector.Detect(folderPath);
        _suppressAutoFill = true;
        if (!_userEditedName && !string.IsNullOrWhiteSpace(detected.Name))
        {
            NameBox.Text = detected.Name;
        }

        if (!_userEditedWorkspace && !string.IsNullOrWhiteSpace(detected.WorkspacePath))
        {
            WorkspaceBox.Text = detected.WorkspacePath;
        }

        if (!_userEditedComment && !string.IsNullOrWhiteSpace(detected.Comment))
        {
            CommentBox.Text = detected.Comment;
        }

        _suppressAutoFill = false;
    }

    private void BrowseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog { Filter = "Cursor workspace|*.code-workspace|Все файлы|*.*" };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) WorkspaceBox.Text = dialog.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            System.Windows.MessageBox.Show(this, "Укажите название и папку.", "WideS");
            return;
        }

        Workspace.Name = NameBox.Text.Trim();
        Workspace.ProjectFolder = FolderBox.Text.Trim();
        Workspace.WorkspacePath = WorkspaceBox.Text.Trim();
        Workspace.Comment = CommentBox.Text.Trim();
        Workspace.Status = StatusBox.SelectedIndex switch
        {
            1 => "Paused",
            2 => "Archive",
            _ => "Active"
        };
        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
