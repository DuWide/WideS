using System.Windows;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;

namespace DevCockpit;

public partial class NoteEditorWindow : Window
{
    private readonly IReadOnlyList<ProjectProfile> _projects;
    private readonly string _originalTitle;
    private readonly string _originalText;
    private readonly bool _originalImportant;
    private readonly Guid? _originalWorkspaceId;
    public bool Saved { get; private set; }
    public NoteItem Note { get; private set; }

    public NoteEditorWindow(
        IReadOnlyList<ProjectProfile> projects,
        NoteItem? source = null)
    {
        InitializeComponent();
        _projects = projects;
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
                SourcePath = source.SourcePath,
                WorkspaceId = source.WorkspaceId
            };
        _originalTitle = Note.Title;
        _originalText = Note.Text;
        _originalImportant = Note.IsImportant;
        _originalWorkspaceId = Note.WorkspaceId;
        LoadData();
    }

    private void LoadData()
    {
        WorkspaceBox.Items.Add("(без привязки)");
        foreach (var item in _projects) WorkspaceBox.Items.Add(item);
        WorkspaceBox.SelectedIndex = 0;
        if (Note.WorkspaceId is not null)
        {
            for (var i = 1; i < WorkspaceBox.Items.Count; i++)
            {
                if (WorkspaceBox.Items[i] is ProjectProfile project && project.Id == Note.WorkspaceId)
                {
                    WorkspaceBox.SelectedIndex = i;
                    break;
                }
            }
        }
        TitleBox.Text = Note.Title;
        TextBox.Text = Note.Text;
        ImportantBox.IsChecked = Note.IsImportant;
    }

    private bool IsDirty()
    {
        var workspaceId = WorkspaceBox.SelectedItem is ProjectProfile project ? project.Id : (Guid?)null;
        return !string.Equals(TitleBox.Text.Trim(), _originalTitle, StringComparison.Ordinal)
               || !string.Equals(TextBox.Text ?? "", _originalText ?? "", StringComparison.Ordinal)
               || (ImportantBox.IsChecked == true) != _originalImportant
               || workspaceId != _originalWorkspaceId;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TrySave(showValidationErrors: true)) Close();
    }

    private bool TrySave(bool showValidationErrors)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            if (showValidationErrors) WpfMessageBox.Show(this, "Укажите заголовок заметки.", "WideS");
            return false;
        }

        Note.Title = TitleBox.Text.Trim();
        Note.Text = TextBox.Text;
        Note.IsImportant = ImportantBox.IsChecked == true;
        Note.WorkspaceId = WorkspaceBox.SelectedItem is ProjectProfile project ? project.Id : null;
        Note.UpdatedAt = DateTime.Now;
        Saved = true;
        return true;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        WpfClipboard.SetText(TextBox.Text);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => EditorWindowHelper.MinimizeWindow(this);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
