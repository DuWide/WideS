namespace DevCockpit;

public static class WorkModeService
{
    public const string Work = "Work";
    public const string Focus = "Focus";
    public const string Leisure = "Leisure";

    public static bool ShowMedia(string mode) => !mode.Equals(Focus, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<(string Label, string Value)> ModeOptions() =>
    [
        ("Стандартный", Work),
        ("Фокус", Focus),
        ("Личный", Leisure)
    ];

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
            return "Фокус: медиапанель скрыта, рабочие разделы остаются доступны.";
        }

        if (mode.Equals(Leisure, StringComparison.OrdinalIgnoreCase))
        {
            return "Личный: рабочие разделы и медиапанель доступны.";
        }

        return "Стандартный: доступны все разделы и медиапанель.";
    }
}
