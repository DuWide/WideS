using System.Windows;
using WpfClipboard = System.Windows.Clipboard;

namespace DevCockpit;

public partial class NoteViewWindow : Window
{
    public bool Saved { get; private set; }
    public Guid NoteId => _note.Id;
    private readonly NoteItem _note;
    private readonly string _originalText;

    public NoteViewWindow(NoteItem note)
    {
        InitializeComponent();
        _note = note;
        _originalText = note.Text ?? "";
        HeaderTitle.Text = string.IsNullOrWhiteSpace(note.Title) ? "(без заголовка)" : note.Title;
        MetaText.Text = $"{note.Category} · {note.UpdatedAt:yyyy-MM-dd HH:mm}";
        BodyText.Text = _originalText;
        UiHelpers.BuildNotePreview(PreviewPanel, _originalText);
        BodyText.TextChanged += (_, _) => UiHelpers.BuildNotePreview(PreviewPanel, BodyText.Text);
        EditorWindowHelper.HookConfirmClose(this, () => !Saved && IsDirty, TrySave);
        Loaded += (_, _) =>
        {
            BodyText.Focus();
            BodyText.CaretIndex = BodyText.Text.Length;
        };
    }

    private bool IsDirty => !string.Equals(BodyText.Text ?? "", _originalText, StringComparison.Ordinal);

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);

    private void Copy_Click(object sender, RoutedEventArgs e) => WpfClipboard.SetText(BodyText.Text ?? "");

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TrySave()) Close();
    }

    private bool TrySave()
    {
        _note.Text = BodyText.Text ?? "";
        _note.UpdatedAt = DateTime.Now;
        Saved = true;
        return true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (EditorWindowHelper.ConfirmClose(this, !Saved && IsDirty, TrySave))
        {
            Close();
        }
    }
}
