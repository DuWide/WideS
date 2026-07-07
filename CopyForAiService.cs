using System.Text;
using WpfClipboard = System.Windows.Clipboard;

namespace DevCockpit;

public static class CopyForAiService
{
    public const int MaxFileBytes = 100 * 1024;
    public const int MaxFiles = 5;

    public static string BuildMarkdown(
        ProjectProfile project,
        string taskText,
        IEnumerable<NoteItem> notes,
        IEnumerable<string> filePaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Задача");
        sb.AppendLine();
        sb.AppendLine(taskText.Trim());
        sb.AppendLine();

        var noteList = notes.ToList();
        if (noteList.Count > 0)
        {
            sb.AppendLine("## Заметки");
            sb.AppendLine();
            foreach (var note in noteList)
            {
                sb.AppendLine($"### {note.Title}");
                sb.AppendLine();
                sb.AppendLine(note.Text.Trim());
                sb.AppendLine();
            }
        }

        var files = filePaths.ToList();
        if (files.Count > 0)
        {
            sb.AppendLine("## Файлы");
            sb.AppendLine();
            foreach (var file in files)
            {
                var relative = Directory.Exists(project.ProjectFolder)
                    ? ProjectScanner.Relative(project.ProjectFolder, file)
                    : Path.GetFileName(file);
                var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(ext))
                {
                    ext = "text";
                }

                sb.AppendLine($"### {relative}");
                sb.AppendLine();
                sb.AppendLine($"```{ext}");
                try
                {
                    var content = File.ReadAllText(file);
                    if (content.Length > MaxFileBytes)
                    {
                        content = content[..MaxFileBytes] + "\n... [обрезано]";
                    }
                    sb.AppendLine(content.TrimEnd());
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[Ошибка чтения: {ex.Message}]");
                }
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string FormatSize(int bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        return $"{bytes / 1024.0:0.#} KB";
    }

    public static bool TryCopyToClipboard(string markdown, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(markdown))
        {
            error = "Нечего копировать.";
            return false;
        }

        try
        {
            WpfClipboard.SetText(markdown);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string? SaveToFile(ProjectProfile project, string markdown)
    {
        var day = AppPaths.EnsureTodayWorkDay(project);
        var contextDir = Path.Combine(day, "Context");
        Directory.CreateDirectory(contextDir);
        var path = Path.Combine(contextDir, $"ai-context_{DateTime.Now:yyyy-MM-dd_HH-mm}.md");
        File.WriteAllText(path, markdown, Encoding.UTF8);
        return path;
    }
}
