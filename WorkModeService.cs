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
        ("Работа", Work),
        ("Фокус", Focus),
        ("Досуг", Leisure)
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
        Leisure => "Досуг",
        _ => "Работа"
    };

    public static string DescribeEffects(string mode)
    {
        if (mode.Equals(Focus, StringComparison.OrdinalIgnoreCase))
        {
            return "Режим: Фокус — только задача, заметка, проект и Cursor; медиа и backup скрыты.";
        }

        if (mode.Equals(Leisure, StringComparison.OrdinalIgnoreCase))
        {
            return "Режим: Досуг — заметка, браузер, DropZone и Dock; минимум рабочих кнопок.";
        }

        return "Режим: Работа — все быстрые действия и медиа-панель доступны.";
    }
}
