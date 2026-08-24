namespace DevCockpit;

public partial class MainWindow
{
    private void CopyForAiSelected()
    {
        var project = RequireProject();
        if (project is null) return;

        var window = new CopyForAiWindow(project, _notes.Notes) { Owner = this };
        if (window.ShowDialog() == true)
        {
            AddLog("OK", window.SavedPath is null
                ? "Текст для AI скопирован в буфер."
                : $"Текст для AI скопирован. Файл: {window.SavedPath}");
        }
    }
}
