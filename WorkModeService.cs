namespace DevCockpit;

public static class WorkModeService
{
    public const string Work = "Work";
    public const string Focus = "Focus";
    public const string Leisure = "Leisure";

    public static bool ShowMedia(string mode) => !mode.Equals(Focus, StringComparison.OrdinalIgnoreCase);

    public static bool ShowBackupActions(string mode) => mode.Equals(Work, StringComparison.OrdinalIgnoreCase);

    public static bool ShowBrowserActions(string mode) => !mode.Equals(Focus, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<(string Label, string Value)> ModeOptions() =>
    [
        ("Стандартный", Work),
        ("Фокус", Focus),
        ("Личный", Leisure)
    ];

    public static IReadOnlyList<(string Label, string Key)> HomeQuickActions(string mode)
    {
        if (mode.Equals(Focus, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ("Заметка", "note"),
                ("Задача", "task"),
                ("Проект", "project"),
                ("Cursor", "cursor")
            ];
        }

        if (mode.Equals(Leisure, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ("Заметка", "note"),
                ("Браузер", "browser"),
                ("DropZone", "dropzone"),
                ("Dock", "dock")
            ];
        }

        return
        [
            ("Заметка", "note"),
            ("Задача", "task"),
            ("Подключение", "connection"),
            ("Команда", "command"),
            ("Backup", "backup"),
            ("Скопировать в AI", "context"),
            ("Отчёт", "report"),
            ("DropZone", "dropzone")
        ];
    }

    public static string DisplayName(string mode) => mode switch
    {
        Focus => "Фокус",
        Leisure => "Личный",
        _ => "Стандартный"
    };

    public static string DescribeEffects(string mode)
    {
        if (mode.Equals(Focus, StringComparison.OrdinalIgnoreCase))
        {
            return "Фокус: задача, заметка, проект и Cursor без второстепенных действий.";
        }

        if (mode.Equals(Leisure, StringComparison.OrdinalIgnoreCase))
        {
            return "Личный: заметки, браузер, DropZone и медиапанель.";
        }

        return "Стандартный: доступны все разделы, быстрые действия и медиапанель.";
    }
}
