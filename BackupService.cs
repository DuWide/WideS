using System.IO.Compression;

namespace DevCockpit;

public static class BackupService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", "node_modules", ".vs", "_WorkDay"
    };

    public sealed record BackupProgress(int Current, int Total, string FileName);

    public sealed record BackupResult(string ZipPath, long SizeBytes, int FileCount, int NewFilesSincePrevious);

    public static string CreateBackup(ProjectProfile project)
        => CreateBackupAsync(project).GetAwaiter().GetResult().ZipPath;

    public static async Task<BackupResult> CreateBackupAsync(
        ProjectProfile project,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = ProjectScanner.EnumerateFilesSafe(project.ProjectFolder)
            .Where(f => !ShouldSkip(project.ProjectFolder, f))
            .ToList();

        var day = AppPaths.EnsureTodayWorkDay(project);
        var backupDir = Path.Combine(day, "Backups");
        Directory.CreateDirectory(backupDir);

        var previousBackupTime = Directory.Exists(backupDir)
            ? Directory.GetFiles(backupDir, "*.zip").Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max()
            : DateTime.MinValue;
        var newFiles = files.Count(f => File.GetLastWriteTime(f) > previousBackupTime);

        var safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(project.Name) ? "Project" : project.Name);
        var zipPath = Path.Combine(backupDir, $"{safeName}_backup_{DateTime.Now:yyyy-MM-dd_HH-mm}.zip");

        await Task.Run(() =>
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            for (var i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i];
                progress?.Report(new BackupProgress(i + 1, files.Count, file));
                var entryName = ProjectScanner.Relative(project.ProjectFolder, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
            }
        }, cancellationToken);

        var size = new FileInfo(zipPath).Length;
        return new BackupResult(zipPath, size, files.Count, newFiles);
    }

    public static string FormatBackupLine(string zipPath, int newFilesSincePrevious = -1)
    {
        var info = new FileInfo(zipPath);
        if (!info.Exists) return zipPath;
        var size = info.Length >= 1024 * 1024
            ? $"{info.Length / (1024 * 1024)} MB"
            : $"{Math.Max(1, info.Length / 1024)} KB";
        var suffix = newFilesSincePrevious >= 0 && newFilesSincePrevious > 0
            ? $" · +{newFilesSincePrevious} новых"
            : string.Empty;
        return $"{info.LastWriteTime:yyyy-MM-dd HH:mm} · {size}{suffix} · {info.Name}";
    }

    public static int CountNewFilesSince(ProjectProfile project, DateTime since)
    {
        return ProjectScanner.EnumerateFilesSafe(project.ProjectFolder)
            .Count(f => !ShouldSkip(project.ProjectFolder, f) && File.GetLastWriteTime(f) > since);
    }

    private static bool ShouldSkip(string root, string file)
    {
        var relative = ProjectScanner.Relative(root, file);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => ExcludedDirectories.Contains(part));
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(ch, '_');
        }

        return value.Trim();
    }
}
