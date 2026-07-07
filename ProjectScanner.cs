namespace DevCockpit;

public static class ProjectScanner
{
    private static readonly string[] SuspiciousWords =
    [
        "password", "pass", "пароль", "логин", "login", "anydesk", "rdp", "доступ", "access", "secret"
    ];

    private static readonly HashSet<string> ResultExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".epf", ".erf", ".cfe", ".cf", ".zip", ".rar", ".7z"
    };

    public static ScanResult Scan(ProjectProfile project)
    {
        var result = new ScanResult();
        if (!Directory.Exists(project.ProjectFolder))
        {
            return result;
        }

        var files = EnumerateFilesSafe(project.ProjectFolder).ToList();
        foreach (var file in files.Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            var info = new FileInfo(file);
            result.TextFiles.Add(new TextFileInfo
            {
                Name = info.Name,
                FullPath = info.FullName,
                RelativePath = Relative(project.ProjectFolder, info.FullName),
                Modified = info.LastWriteTime,
                Size = info.Length,
                IsSuspicious = IsSuspiciousTextName(info.Name)
            });
        }

        result.Workspaces.AddRange(files
            .Where(f => f.EndsWith(".code-workspace", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .Select(f => Relative(project.ProjectFolder, f)));

        result.ResultFiles.AddRange(files
            .Where(f => ResultExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f)
            .Select(f => Relative(project.ProjectFolder, f)));

        result.XmlFolders.AddRange(files
            .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .GroupBy(Path.GetDirectoryName)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new XmlFolderInfo
            {
                FullPath = g.Key!,
                RelativePath = Relative(project.ProjectFolder, g.Key!),
                Count = g.Count(),
                LastModified = g.Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max()
            })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count));

        return result;
    }

    public static bool IsSuspiciousTextName(string name)
    {
        return SuspiciousWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> subdirs;
            IEnumerable<string> files;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var subdir in subdirs)
            {
                pending.Push(subdir);
            }
        }
    }

    public static string Relative(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(root, path);
        }
        catch
        {
            return path;
        }
    }
}
