using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace DevCockpit;

public static class ThemeService
{
    public static readonly WpfColor FixedActionBg = WpfColor.FromRgb(22, 58, 72);
    public static readonly WpfColor FixedActionSoftBg = WpfColor.FromRgb(18, 48, 60);
    public static readonly WpfColor FixedActionBorder = WpfColor.FromRgb(56, 130, 150);
    public static readonly WpfColor FixedPrimaryButtonBg = WpfColor.FromRgb(20, 52, 66);

    public sealed record ThemePreset(
        string Id,
        string DisplayName,
        WpfColor AppBg,
        WpfColor SidebarBg,
        WpfColor PanelBg,
        WpfColor CardBg,
        WpfColor CardHover,
        WpfColor Accent,
        WpfColor AccentDark,
        WpfColor AccentSoftBg,
        WpfColor AccentBorder,
        WpfColor TextMain,
        WpfColor TextMuted,
        WpfColor BorderMain,
        WpfColor BorderSubtle,
        WpfColor FilterBg,
        WpfColor FilterBorder,
        WpfColor TealSoftBg,
        WpfColor TealBorder,
        WpfColor FocusWarnBg,
        WpfColor PrimaryButtonBg,
        WpfColor GhostButtonBg,
        WpfColor GhostButtonBorder,
        WpfColor WindowButtonBg,
        WpfColor WindowButtonBorder,
        WpfColor NavHoverBg,
        WpfColor NavActiveHoverBg,
        WpfColor ScrollThumb,
        WpfColor ScrollThumbHover,
        WpfColor SidebarFooterBg,
        WpfColor LogPanelBg);

    public static IReadOnlyList<ThemePreset> Presets { get; } =
    [
        new("Ocean", "Океан",
            WpfColor.FromRgb(14, 20, 28),
            WpfColor.FromRgb(18, 26, 36),
            WpfColor.FromRgb(26, 36, 48),
            WpfColor.FromRgb(32, 44, 58),
            WpfColor.FromRgb(40, 54, 70),
            WpfColor.FromRgb(72, 196, 240),
            WpfColor.FromRgb(46, 132, 170),
            WpfColor.FromRgb(18, 52, 68),
            WpfColor.FromRgb(36, 92, 118),
            WpfColor.FromRgb(232, 242, 248),
            WpfColor.FromRgb(148, 168, 188),
            WpfColor.FromRgb(52, 68, 86),
            WpfColor.FromRgb(24, 34, 44),
            WpfColor.FromRgb(20, 52, 72),
            WpfColor.FromRgb(72, 170, 220),
            WpfColor.FromRgb(16, 44, 58),
            WpfColor.FromRgb(32, 96, 112),
            WpfColor.FromRgb(52, 40, 20),
            WpfColor.FromRgb(22, 58, 76),
            WpfColor.FromRgb(28, 38, 50),
            WpfColor.FromRgb(52, 68, 84),
            WpfColor.FromRgb(36, 48, 62),
            WpfColor.FromRgb(44, 58, 74),
            WpfColor.FromRgb(28, 40, 52),
            WpfColor.FromRgb(24, 36, 48),
            WpfColor.FromRgb(58, 72, 88),
            WpfColor.FromRgb(72, 88, 104),
            WpfColor.FromRgb(20, 30, 40),
            WpfColor.FromRgb(12, 18, 26)),
        new("Graphite", "Графит",
            WpfColor.FromRgb(12, 12, 14),
            WpfColor.FromRgb(18, 18, 21),
            WpfColor.FromRgb(28, 28, 32),
            WpfColor.FromRgb(34, 34, 39),
            WpfColor.FromRgb(42, 42, 48),
            WpfColor.FromRgb(176, 184, 196),
            WpfColor.FromRgb(128, 136, 148),
            WpfColor.FromRgb(36, 38, 44),
            WpfColor.FromRgb(72, 78, 88),
            WpfColor.FromRgb(236, 238, 242),
            WpfColor.FromRgb(148, 154, 164),
            WpfColor.FromRgb(54, 56, 62),
            WpfColor.FromRgb(22, 22, 26),
            WpfColor.FromRgb(40, 42, 48),
            WpfColor.FromRgb(108, 116, 128),
            WpfColor.FromRgb(32, 34, 40),
            WpfColor.FromRgb(36, 40, 46),
            WpfColor.FromRgb(88, 72, 48),
            WpfColor.FromRgb(48, 50, 58),
            WpfColor.FromRgb(30, 32, 38),
            WpfColor.FromRgb(52, 54, 62),
            WpfColor.FromRgb(40, 42, 48),
            WpfColor.FromRgb(56, 58, 66),
            WpfColor.FromRgb(34, 36, 42),
            WpfColor.FromRgb(28, 30, 36),
            WpfColor.FromRgb(62, 66, 74),
            WpfColor.FromRgb(78, 82, 90),
            WpfColor.FromRgb(16, 16, 19),
            WpfColor.FromRgb(10, 10, 12)),
        new("Forest", "Лес",
            WpfColor.FromRgb(10, 18, 14),
            WpfColor.FromRgb(14, 26, 20),
            WpfColor.FromRgb(20, 36, 28),
            WpfColor.FromRgb(26, 44, 34),
            WpfColor.FromRgb(32, 54, 42),
            WpfColor.FromRgb(88, 210, 140),
            WpfColor.FromRgb(56, 148, 96),
            WpfColor.FromRgb(16, 44, 32),
            WpfColor.FromRgb(40, 96, 72),
            WpfColor.FromRgb(228, 244, 234),
            WpfColor.FromRgb(136, 164, 148),
            WpfColor.FromRgb(44, 72, 58),
            WpfColor.FromRgb(18, 32, 26),
            WpfColor.FromRgb(18, 48, 36),
            WpfColor.FromRgb(72, 180, 120),
            WpfColor.FromRgb(14, 36, 28),
            WpfColor.FromRgb(28, 72, 58),
            WpfColor.FromRgb(56, 44, 18),
            WpfColor.FromRgb(20, 54, 40),
            WpfColor.FromRgb(22, 38, 30),
            WpfColor.FromRgb(44, 68, 56),
            WpfColor.FromRgb(34, 56, 44),
            WpfColor.FromRgb(40, 64, 52),
            WpfColor.FromRgb(24, 40, 32),
            WpfColor.FromRgb(20, 34, 28),
            WpfColor.FromRgb(52, 76, 62),
            WpfColor.FromRgb(68, 92, 76),
            WpfColor.FromRgb(12, 22, 18),
            WpfColor.FromRgb(8, 14, 11)),
        new("Ember", "Янтарь",
            WpfColor.FromRgb(22, 14, 10),
            WpfColor.FromRgb(30, 20, 14),
            WpfColor.FromRgb(42, 28, 20),
            WpfColor.FromRgb(52, 34, 24),
            WpfColor.FromRgb(62, 42, 30),
            WpfColor.FromRgb(244, 176, 72),
            WpfColor.FromRgb(196, 128, 44),
            WpfColor.FromRgb(58, 36, 16),
            WpfColor.FromRgb(128, 84, 36),
            WpfColor.FromRgb(248, 236, 224),
            WpfColor.FromRgb(184, 156, 132),
            WpfColor.FromRgb(88, 58, 40),
            WpfColor.FromRgb(34, 22, 16),
            WpfColor.FromRgb(68, 40, 20),
            WpfColor.FromRgb(220, 148, 56),
            WpfColor.FromRgb(48, 28, 14),
            WpfColor.FromRgb(72, 48, 28),
            WpfColor.FromRgb(72, 48, 20),
            WpfColor.FromRgb(74, 48, 22),
            WpfColor.FromRgb(36, 24, 16),
            WpfColor.FromRgb(58, 40, 28),
            WpfColor.FromRgb(84, 58, 40),
            WpfColor.FromRgb(68, 48, 34),
            WpfColor.FromRgb(44, 30, 20),
            WpfColor.FromRgb(38, 26, 18),
            WpfColor.FromRgb(96, 68, 46),
            WpfColor.FromRgb(112, 82, 56),
            WpfColor.FromRgb(24, 16, 12),
            WpfColor.FromRgb(16, 10, 8)),
        new("Amethyst", "Аметист",
            WpfColor.FromRgb(14, 10, 22),
            WpfColor.FromRgb(20, 14, 30),
            WpfColor.FromRgb(28, 20, 40),
            WpfColor.FromRgb(34, 26, 48),
            WpfColor.FromRgb(42, 32, 58),
            WpfColor.FromRgb(168, 128, 220),
            WpfColor.FromRgb(118, 86, 168),
            WpfColor.FromRgb(32, 22, 48),
            WpfColor.FromRgb(72, 52, 108),
            WpfColor.FromRgb(236, 228, 246),
            WpfColor.FromRgb(156, 140, 180),
            WpfColor.FromRgb(56, 42, 78),
            WpfColor.FromRgb(22, 16, 32),
            WpfColor.FromRgb(38, 26, 58),
            WpfColor.FromRgb(132, 96, 188),
            WpfColor.FromRgb(24, 16, 36),
            WpfColor.FromRgb(44, 30, 64),
            WpfColor.FromRgb(58, 40, 20),
            WpfColor.FromRgb(28, 18, 42),
            WpfColor.FromRgb(34, 24, 48),
            WpfColor.FromRgb(52, 38, 70),
            WpfColor.FromRgb(40, 28, 56),
            WpfColor.FromRgb(48, 34, 68),
            WpfColor.FromRgb(30, 20, 44),
            WpfColor.FromRgb(26, 18, 38),
            WpfColor.FromRgb(64, 48, 86),
            WpfColor.FromRgb(80, 62, 102),
            WpfColor.FromRgb(18, 12, 28),
            WpfColor.FromRgb(10, 8, 16))
    ];

    public static string NormalizePresetId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Ocean";
        }

        return value switch
        {
            "Blue" => "Ocean",
            "Purple" => "Amethyst",
            "Neutral" => "Graphite",
            _ when Presets.Any(p => p.Id.Equals(value, StringComparison.OrdinalIgnoreCase)) => Presets.First(p => p.Id.Equals(value, StringComparison.OrdinalIgnoreCase)).Id,
            _ => "Ocean"
        };
    }

    public static ThemePreset GetPreset(string? value) =>
        Presets.First(p => p.Id == NormalizePresetId(value));

    public static void Apply(string? presetId)
    {
        var preset = GetPreset(presetId);
        var resources = WpfApplication.Current.Resources;

        SetColor(resources, "AppBg", preset.AppBg);
        SetColor(resources, "SidebarBg", preset.SidebarBg);
        SetColor(resources, "PanelBg", preset.PanelBg);
        SetColor(resources, "CardBg", preset.CardBg);
        SetColor(resources, "CardHover", preset.CardHover);
        SetColor(resources, "Accent", preset.Accent);
        SetColor(resources, "AccentDark", preset.AccentDark);
        SetColor(resources, "TextMain", preset.TextMain);
        SetColor(resources, "TextMuted", preset.TextMuted);
        SetColor(resources, "AccentSoftBg", preset.AccentSoftBg);
        SetColor(resources, "AccentBorder", preset.AccentBorder);
        SetColor(resources, "BorderMain", preset.BorderMain);
        SetColor(resources, "BorderSubtle", preset.BorderSubtle);
        SetColor(resources, "FilterBg", preset.FilterBg);
        SetColor(resources, "FilterBorder", preset.FilterBorder);
        SetColor(resources, "TealSoftBg", preset.TealSoftBg);
        SetColor(resources, "TealBorder", preset.TealBorder);
        SetColor(resources, "FocusWarnBg", preset.FocusWarnBg);
        SetColor(resources, "PrimaryButtonBg", FixedPrimaryButtonBg);
        SetColor(resources, "ActionBg", FixedActionBg);
        SetColor(resources, "ActionSoftBg", FixedActionSoftBg);
        SetColor(resources, "ActionBorder", FixedActionBorder);
        SetColor(resources, "GhostButtonBg", preset.GhostButtonBg);
        SetColor(resources, "GhostButtonBorder", preset.GhostButtonBorder);
        SetColor(resources, "WindowButtonBg", preset.WindowButtonBg);
        SetColor(resources, "WindowButtonBorder", preset.WindowButtonBorder);
        SetColor(resources, "NavHoverBg", preset.NavHoverBg);
        SetColor(resources, "NavActiveHoverBg", preset.NavActiveHoverBg);
        SetColor(resources, "ScrollThumb", preset.ScrollThumb);
        SetColor(resources, "ScrollThumbHover", preset.ScrollThumbHover);
        SetColor(resources, "SidebarFooterBg", preset.SidebarFooterBg);
        SetColor(resources, "LogPanelBg", preset.LogPanelBg);

        SetBrush(resources, "AppBgBrush", preset.AppBg);
        SetBrush(resources, "SidebarBrush", preset.SidebarBg);
        SetBrush(resources, "PanelBrush", preset.PanelBg);
        SetBrush(resources, "CardBrush", preset.CardBg);
        SetBrush(resources, "CardHoverBrush", preset.CardHover);
        SetBrush(resources, "AccentBrush", preset.Accent);
        SetBrush(resources, "AccentDarkBrush", preset.AccentDark);
        SetBrush(resources, "TextBrush", preset.TextMain);
        SetBrush(resources, "MutedBrush", preset.TextMuted);
        SetBrush(resources, "AccentSoftBgBrush", preset.AccentSoftBg);
        SetBrush(resources, "AccentBorderBrush", preset.AccentBorder);
        SetBrush(resources, "BorderMainBrush", preset.BorderMain);
        SetBrush(resources, "BorderSubtleBrush", preset.BorderSubtle);
        SetBrush(resources, "FilterBgBrush", preset.FilterBg);
        SetBrush(resources, "FilterBorderBrush", preset.FilterBorder);
        SetBrush(resources, "TealSoftBgBrush", preset.TealSoftBg);
        SetBrush(resources, "TealBorderBrush", preset.TealBorder);
        SetBrush(resources, "FocusWarnBgBrush", preset.FocusWarnBg);
        SetBrush(resources, "PrimaryButtonBgBrush", FixedPrimaryButtonBg);
        SetBrush(resources, "ActionBgBrush", FixedActionBg);
        SetBrush(resources, "ActionSoftBgBrush", FixedActionSoftBg);
        SetBrush(resources, "ActionBorderBrush", FixedActionBorder);
        SetBrush(resources, "GhostButtonBgBrush", preset.GhostButtonBg);
        SetBrush(resources, "GhostButtonBorderBrush", preset.GhostButtonBorder);
        SetBrush(resources, "WindowButtonBgBrush", preset.WindowButtonBg);
        SetBrush(resources, "WindowButtonBorderBrush", preset.WindowButtonBorder);
        SetBrush(resources, "NavHoverBgBrush", preset.NavHoverBg);
        SetBrush(resources, "NavActiveHoverBgBrush", preset.NavActiveHoverBg);
        SetBrush(resources, "ScrollThumbBrush", preset.ScrollThumb);
        SetBrush(resources, "ScrollThumbHoverBrush", preset.ScrollThumbHover);
        SetBrush(resources, "SidebarFooterBgBrush", preset.SidebarFooterBg);
        SetBrush(resources, "LogPanelBgBrush", preset.LogPanelBg);
    }

    private static void SetColor(ResourceDictionary resources, string key, WpfColor color) =>
        resources[key] = color;

    private static void SetBrush(ResourceDictionary resources, string key, WpfColor color) =>
        resources[key] = new SolidColorBrush(color);
}
