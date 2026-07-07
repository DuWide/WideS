using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfMessageBox = System.Windows.MessageBox;

namespace DevCockpit;

public partial class CopyForAiWindow : Window
{
    private readonly ProjectProfile _project;
    private readonly List<NoteItem> _projectNotes;
    private readonly List<string> _selectedFiles = [];

    public bool Copied { get; private set; }
    public string? SavedPath { get; private set; }

    public CopyForAiWindow(ProjectProfile project, IEnumerable<NoteItem> notes)
    {
        InitializeComponent();
        _project = project;
        _projectNotes = notes.Where(n => n.WorkspaceId == project.Id).OrderByDescending(n => n.UpdatedAt).ToList();
        LoadNotes();
        TaskBox.TextChanged += (_, _) => UpdateSizePreview();
        UpdateSizePreview();
    }

    private void LoadNotes()
    {
        NotesList.Items.Clear();
        foreach (var note in _projectNotes)
        {
            var check = new WpfCheckBox
            {
                Content = note.Title,
                Tag = note,
                Margin = new Thickness(4, 2, 4, 2),
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush")
            };
            check.Checked += (_, _) => UpdateSizePreview();
            check.Unchecked += (_, _) => UpdateSizePreview();
            NotesList.Items.Add(check);
        }

        if (_projectNotes.Count == 0)
        {
            NotesList.Items.Add(new TextBlock
            {
                Text = "Нет заметок проекта.",
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                Margin = new Thickness(8)
            });
        }
    }

    private void RefreshFilesList()
    {
        FilesList.Items.Clear();
        foreach (var file in _selectedFiles)
        {
            var info = new FileInfo(file);
            var row = new DockPanel { Margin = new Thickness(4, 2, 4, 2) };
            var label = new TextBlock
            {
                Text = $"{ProjectScanner.Relative(_project.ProjectFolder, file)} ({CopyForAiService.FormatSize((int)Math.Min(info.Length, CopyForAiService.MaxFileBytes))})",
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(label, Dock.Left);
            var remove = new WpfButton
            {
                Content = "✕",
                Width = 28,
                Height = 24,
                Tag = file,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            remove.Click += (_, _) =>
            {
                _selectedFiles.Remove(file);
                RefreshFilesList();
                UpdateSizePreview();
            };
            row.Children.Add(label);
            row.Children.Add(remove);
            FilesList.Items.Add(row);
        }
    }

    private IEnumerable<NoteItem> GetSelectedNotes()
    {
        foreach (var item in NotesList.Items)
        {
            if (item is WpfCheckBox { IsChecked: true, Tag: NoteItem note })
            {
                yield return note;
            }
        }
    }

    private string BuildMarkdown()
    {
        return CopyForAiService.BuildMarkdown(
            _project,
            TaskBox.Text,
            GetSelectedNotes(),
            _selectedFiles);
    }

    private void UpdateSizePreview()
    {
        var markdown = BuildMarkdown();
        SizeLabel.Text = $"Размер: {CopyForAiService.FormatSize(System.Text.Encoding.UTF8.GetByteCount(markdown))}";
    }

    private bool ValidateTask(out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(TaskBox.Text))
        {
            error = "Опишите задачу для AI.";
            return false;
        }

        return true;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateTask(out var error))
        {
            WpfMessageBox.Show(this, error, "WideS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var markdown = BuildMarkdown();
        if (!CopyForAiService.TryCopyToClipboard(markdown, out error))
        {
            WpfMessageBox.Show(this, error, "WideS", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Copied = true;
        DialogResult = true;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateTask(out var error))
        {
            WpfMessageBox.Show(this, error, "WideS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var markdown = BuildMarkdown();
        SavedPath = CopyForAiService.SaveToFile(_project, markdown);
        if (CopyForAiService.TryCopyToClipboard(markdown, out _))
        {
            Copied = true;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите файлы для AI",
            Filter = "Код и текст (*.bsl;*.xml;*.cs;*.md;*.txt)|*.bsl;*.xml;*.cs;*.md;*.txt|Все файлы (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            if (_selectedFiles.Count >= CopyForAiService.MaxFiles)
            {
                WpfMessageBox.Show(this, $"Не больше {CopyForAiService.MaxFiles} файлов.", "WideS");
                break;
            }

            if (_selectedFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var info = new FileInfo(file);
            if (info.Length > CopyForAiService.MaxFileBytes)
            {
                var confirm = WpfMessageBox.Show(this,
                    $"{info.Name} больше 100 KB. Будет обрезан при копировании. Добавить?",
                    "WideS", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                {
                    continue;
                }
            }

            _selectedFiles.Add(file);
        }

        RefreshFilesList();
        UpdateSizePreview();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        DragMove();
    }
}
