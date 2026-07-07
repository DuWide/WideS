using System.Text.Encodings.Web;
using System.Text.Json;

namespace DevCockpit;

public sealed class ProjectStore
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ProjectStoreData Load()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        if (!File.Exists(AppPaths.ProjectsJson))
        {
            var empty = new ProjectStoreData();
            Save(empty);
            return empty;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.ProjectsJson);
            return JsonSerializer.Deserialize<ProjectStoreData>(json, _options) ?? new ProjectStoreData();
        }
        catch
        {
            return new ProjectStoreData();
        }
    }

    public void Save(ProjectStoreData data)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(AppPaths.ProjectsJson, JsonSerializer.Serialize(data, _options));
    }
}
