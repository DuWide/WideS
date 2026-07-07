using System.Windows;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;

namespace DevCockpit;

public partial class NoteEditorWindow : Window
{
    private readonly IReadOnlyList<ProjectProfile> _projects;
    private readonly Func<NoteItem, IReadOnlyList<ConnectionItem>, int>? _saveDetectedConnections;
    private readonly string _originalTitle;
    private readonly string _originalText;
    private readonly string _originalCategory;
    private readonly bool _originalImportant;
    private readonly Guid? _originalWorkspaceId;
    public bool Saved { get; private set; }
    public NoteItem Note { get; private set; }

    public NoteEditorWindow(
        IReadOnlyList<ProjectProfile> projects,
        NoteItem? source = null,
        Func<NoteItem, IReadOnlyList<ConnectionItem>, int>? saveDetectedConnections = null)
    {
        InitializeComponent();
        _projects = projects;
        _saveDetectedConnections = saveDetectedConnections;
        Note = source is null
            ? new NoteItem()
            : new NoteItem
            {
                Id = source.Id,
                Title = source.Title,
                Category = source.Category,
                Text = source.Text,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                IsImportant = source.IsImportant,
                IsPinned = source.IsPinned,
                SourcePath = source.SourcePath,
                WorkspaceId = source.WorkspaceId
            };
        _originalTitle = Note.Title;
        _originalText = Note.Text;
        _originalCategory = Note.Category;
        _originalImportant = Note.IsImportant;
        _originalWorkspaceId = Note.WorkspaceId;
        LoadData();
        EditorWindowHelper.HookConfirmClose(this, () => !Saved && IsDirty(), () => TrySave(showValidationErrors: true));
    }

    private void LoadData()
    {
        CategoryBox.ItemsSource = new[] { "Общее", "Доступы", "Ошибки", "Решения", "Команды", "HTTP", "1С", "Временное" };
        CategoryBox.SelectedItem = string.IsNullOrWhiteSpace(Note.Category) ? "Общее" : Note.Category;
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
               || !string.Equals(CategoryBox.SelectedItem?.ToString() ?? "Общее", string.IsNullOrWhiteSpace(_originalCategory) ? "Общее" : _originalCategory, StringComparison.Ordinal)
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
        Note.Category = CategoryBox.SelectedItem?.ToString() ?? "Общее";
        Note.Text = TextBox.Text;
        Note.IsImportant = ImportantBox.IsChecked == true;
        Note.WorkspaceId = WorkspaceBox.SelectedItem is ProjectProfile project ? project.Id : null;
        Note.UpdatedAt = DateTime.Now;
        Saved = true;
        return true;
    }

    private void DetectConnections_Click(object sender, RoutedEventArgs e)
    {
        if (_saveDetectedConnections is null)
        {
            WpfMessageBox.Show(this, "Сохранение подключений недоступно.", "WideS");
            return;
        }

        var draft = BuildDraftNote();
        var detected = NoteConnectionDetector.Detect(TextBox.Text, string.IsNullOrWhiteSpace(TitleBox.Text) ? "Заметка" : TitleBox.Text.Trim());
        if (detected.Count == 0)
        {
            WpfMessageBox.Show(this, "В тексте заметки не найдено адресов, логинов или паролей для подключения.", "WideS");
            return;
        }

        var preview = string.Join("\n", detected.Select(item =>
            $"• {item.Name} [{item.Type}] {item.Address}" +
            (string.IsNullOrWhiteSpace(item.Login) ? "" : $" / {item.Login}")));
        var result = WpfMessageBox.Show(this,
            $"Найдено подключений: {detected.Count}\n\n{preview}\n\nСоздать?",
            "WideS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var created = _saveDetectedConnections(draft, detected);
        WpfMessageBox.Show(this,
            created > 0 ? $"Создано подключений: {created}." : "Новых подключений не создано — такие уже есть.",
            "WideS");
    }

    private NoteItem BuildDraftNote()
    {
        return new NoteItem
        {
            Title = TitleBox.Text.Trim(),
            Category = CategoryBox.SelectedItem?.ToString() ?? "Общее",
            Text = TextBox.Text,
            IsImportant = ImportantBox.IsChecked == true,
            WorkspaceId = WorkspaceBox.SelectedItem is ProjectProfile project ? project.Id : null
        };
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        WpfClipboard.SetText(TextBox.Text);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => EditorWindowHelper.MinimizeWindow(this);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (EditorWindowHelper.ConfirmClose(this, !Saved && IsDirty(), () => TrySave(showValidationErrors: true)))
        {
            Close();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
