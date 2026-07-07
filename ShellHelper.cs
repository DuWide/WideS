using System.Diagnostics;
using WpfClipboard = System.Windows.Clipboard;

namespace DevCockpit;

public static class ShellHelper
{
    public static Process? OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public static string OpenWithEditor(ProjectProfile project, string? workspaceFromScan = null)
    {
        var workspace = !string.IsNullOrWhiteSpace(project.WorkspacePath) && File.Exists(project.WorkspacePath)
            ? project.WorkspacePath
            : workspaceFromScan;

        if (string.IsNullOrWhiteSpace(workspace) &&
            !string.IsNullOrWhiteSpace(project.EditorPath) &&
            File.Exists(project.EditorPath) &&
            (project.EditorPath.EndsWith(".code-workspace", StringComparison.OrdinalIgnoreCase) || IsLikelyWorkspaceFile(project.EditorPath)))
        {
            workspace = project.EditorPath;
        }

        if (!string.IsNullOrWhiteSpace(project.EditorPath) &&
            File.Exists(project.EditorPath) &&
            !project.EditorPath.EndsWith(".code-workspace", StringComparison.OrdinalIgnoreCase) &&
            !IsLikelyWorkspaceFile(project.EditorPath))
        {
            try
            {
                var target = !string.IsNullOrWhiteSpace(workspace) ? workspace : project.ProjectFolder;
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = project.EditorPath,
                    Arguments = $"\"{target}\"",
                    UseShellExecute = false,
                    WorkingDirectory = Directory.Exists(project.ProjectFolder) ? project.ProjectFolder : ""
                });
                WindowPlacementService.MoveProcessToSecondaryAsync(process, project.EditorPath, "Cursor", "Code");
                return "Cursor/редактор запущен.";
            }
            catch (Exception ex)
            {
                return $"Не удалось запустить редактор: {ex.Message}";
            }
        }

        if (!string.IsNullOrWhiteSpace(workspace) && File.Exists(workspace))
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = workspace,
                    UseShellExecute = true,
                    WorkingDirectory = Directory.Exists(project.ProjectFolder) ? project.ProjectFolder : ""
                });
                WindowPlacementService.MoveProcessToSecondaryAsync(process, "Cursor", "Code");
                return "Workspace открыт через системную ассоциацию.";
            }
            catch (Exception ex)
            {
                WpfClipboard.SetText(workspace);
                return $"Не удалось открыть workspace: {ex.Message}. Путь скопирован в буфер.";
            }
        }

        if (Directory.Exists(project.ProjectFolder))
        {
            var folderProcess = OpenPath(project.ProjectFolder);
            WindowPlacementService.MoveProcessToPrimaryAsync(folderProcess, "explorer");
            return "Открыта папка проекта.";
        }

        return "Не найден workspace или папка проекта.";
    }

    private static bool IsLikelyWorkspaceFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 128 * 1024) return false;
            var text = File.ReadAllText(path);
            return text.Contains("\"folders\"", StringComparison.OrdinalIgnoreCase) &&
                   text.Contains("\"settings\"", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
