using System.Text.Json;
using System.Xml.Linq;

namespace DevCockpit;

public sealed class ProjectFolderDetection
{
    public string Name { get; set; } = "";
    public string WorkspacePath { get; set; } = "";
    public string Comment { get; set; } = "";
}

public static class ProjectFolderDetector
{
    public static ProjectFolderDetection Detect(string folderPath)
    {
        var result = new ProjectFolderDetection();
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return result;
        }

        var folderName = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
        result.Name = folderName;

        var readmePath = Path.Combine(folderPath, "README.md");
        if (File.Exists(readmePath))
        {
            try
            {
                var lines = File.ReadLines(readmePath).Take(20).ToList();
                var title = lines
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.StartsWith('#'));
                if (!string.IsNullOrWhiteSpace(title))
                {
                    result.Name = title.TrimStart('#').Trim();
                }

                var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'));
                if (!string.IsNullOrWhiteSpace(firstLine))
                {
                    result.Comment = firstLine.Trim();
                }
            }
            catch
            {
                // ignore read errors
            }
        }

        try
        {
            var workspace = Directory.EnumerateFiles(folderPath, "*.code-workspace", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(workspace))
            {
                result.WorkspacePath = workspace;
            }
        }
        catch
        {
            // ignore
        }

        var packageJson = Path.Combine(folderPath, "package.json");
        if (File.Exists(packageJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
                if (doc.RootElement.TryGetProperty("name", out var nameProp)
                    && nameProp.ValueKind == JsonValueKind.String)
                {
                    var pkgName = nameProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pkgName))
                    {
                        result.Name = pkgName;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            var csproj = Directory.EnumerateFiles(folderPath, "*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(csproj))
            {
                var csprojName = TryReadAssemblyName(csproj) ?? Path.GetFileNameWithoutExtension(csproj);
                if (!string.IsNullOrWhiteSpace(csprojName))
                {
                    result.Name = csprojName;
                }
            }
        }
        catch
        {
            // ignore
        }

        if (string.IsNullOrWhiteSpace(result.Name))
        {
            result.Name = folderName;
        }

        return result;
    }

    private static string? TryReadAssemblyName(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            var assemblyName = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                return assemblyName;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
