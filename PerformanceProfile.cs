using System.Windows;
using System.Windows.Media;

namespace DevCockpit;

/// <summary>
/// Режим производительности. «Авто» включает экономный профиль, если у видеокарты нет полноценного
/// аппаратного ускорения WPF (Render Tier &lt; 2), в RDP-сеансе или при включённой экономии заряда.
/// Экономный профиль отключает бесконечную анимацию свечения темы Raycast и тень Floating Dock;
/// внешний вид (цвета, раскладка) не меняется.
/// </summary>
public static class PerformanceProfile
{
    public const string Auto = "Auto";
    public const string Quality = "Quality";
    public const string Economy = "Economy";

    public static bool ReducedEffects { get; private set; }

    public static string AutoReason { get; private set; } = "";

    public static IReadOnlyList<(string Label, string Value)> ModeOptions() =>
    [
        ("Авто (по возможностям ПК)", Auto),
        ("Качество — все эффекты", Quality),
        ("Производительность — без анимаций и теней", Economy)
    ];

    public static string Normalize(string? mode)
    {
        if (string.Equals(mode, Quality, StringComparison.OrdinalIgnoreCase)) return Quality;
        if (string.Equals(mode, Economy, StringComparison.OrdinalIgnoreCase)) return Economy;
        return Auto;
    }

    public static void Apply(string? mode)
    {
        ReducedEffects = Normalize(mode) switch
        {
            Economy => true,
            Quality => false,
            _ => DetectConstrainedSystem()
        };
    }

    public static string Describe(string? mode)
    {
        var normalized = Normalize(mode);
        if (normalized == Economy) return "Анимация свечения и тени отключены.";
        if (normalized == Quality) return "Все эффекты включены.";
        DetectConstrainedSystem();
        return string.IsNullOrEmpty(AutoReason)
            ? "Авто: ПК справляется, эффекты включены."
            : $"Авто: включён экономный режим ({AutoReason}).";
    }

    private static bool DetectConstrainedSystem()
    {
        AutoReason = "";
        try
        {
            var tier = RenderCapability.Tier >> 16;
            if (tier < 2)
            {
                AutoReason = tier == 0 ? "нет аппаратного ускорения графики" : "слабое аппаратное ускорение графики";
                return true;
            }
        }
        catch
        {
            // Не удалось определить — считаем, что ускорение есть.
        }

        if (SystemParameters.IsRemoteSession)
        {
            AutoReason = "удалённый рабочий стол";
            return true;
        }

        try
        {
            if (global::Windows.System.Power.PowerManager.EnergySaverStatus == global::Windows.System.Power.EnergySaverStatus.On)
            {
                AutoReason = "включена экономия заряда";
                return true;
            }
        }
        catch
        {
            // API недоступно — пропускаем проверку.
        }

        return false;
    }
}
