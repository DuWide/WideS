namespace DevCockpit;

public static class AppPaths
{
    private static readonly string LegacyAppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DevCockpit");

    public static string DataDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("WIDES_DATA_DIR");
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WideS")
                : Path.GetFullPath(overridePath);
        }
    }

    public static string LegacyDataDirectory => Path.Combine(AppContext.BaseDirectory, "data");
    public static string ProjectsJson => Path.Combine(DataDirectory, "projects.json");
    public static string NotesJson => Path.Combine(DataDirectory, "notes.json");
    public static string ConnectionsJson => Path.Combine(DataDirectory, "connections.json");
    public static string SettingsJson => Path.Combine(DataDirectory, "settings.json");
    public static string TasksJson => Path.Combine(DataDirectory, "tasks.json");
    public static string ActivityJson => Path.Combine(DataDirectory, "activity.json");
    public static string ProjectTemplatesJson => Path.Combine(DataDirectory, "project-templates.json");
    public static string WebAppsDirectory => Path.Combine(DataDirectory, "web-apps");

    public static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(WebAppsDirectory);
        MigrateFromLegacyAppData();
        MigrateIfMissing("projects.json");
        MigrateIfMissing("notes.json");
        MigrateIfMissing("connections.json");
        MigrateIfMissing("settings.json");
        MigrateIfMissing("tasks.json");
        MigrateIfMissing("activity.json");
        MigrateIfMissing("project-templates.json");
    }

    private static void MigrateFromLegacyAppData()
    {
        if (!Directory.Exists(LegacyAppDataDirectory)) return;
        foreach (var file in Directory.GetFiles(LegacyAppDataDirectory, "*.json"))
        {
            var target = Path.Combine(DataDirectory, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target);
            }
        }
    }

    private static void MigrateIfMissing(string fileName)
    {
        var target = Path.Combine(DataDirectory, fileName);
        var legacy = Path.Combine(LegacyDataDirectory, fileName);
        if (!File.Exists(target) && File.Exists(legacy))
        {
            File.Copy(legacy, target);
        }
    }

    public static string TodayWorkDay(ProjectProfile project)
    {
        return Path.Combine(project.ProjectFolder, "_WorkDay", DateTime.Now.ToString("yyyy-MM-dd"));
    }

    public static string EnsureTodayWorkDay(ProjectProfile project)
    {
        var day = TodayWorkDay(project);
        foreach (var name in new[] { "Screens", "Errors", "Temp", "Context", "Releases" })
        {
            Directory.CreateDirectory(Path.Combine(day, name));
        }

        return day;
    }
}
