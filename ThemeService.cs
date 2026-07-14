using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace DevCockpit;

public static class ThemeService
{
    private static readonly IReadOnlyDictionary<string, string> BrushAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppBg"] = "AppBgBrush",
            ["SidebarBg"] = "SidebarBrush",
            ["PanelBg"] = "PanelBrush",
            ["CardBg"] = "CardBrush",
            ["CardHover"] = "CardHoverBrush",
            ["InputBg"] = "InputBgBrush",
            ["ElevatedBg"] = "ElevatedBgBrush",
            ["Accent"] = "AccentBrush",
            ["AccentDark"] = "AccentDarkBrush",
            ["AccentHover"] = "AccentHoverBrush",
            ["AccentPressed"] = "AccentPressedBrush",
            ["AccentForeground"] = "AccentForegroundBrush",
            ["Icon"] = "IconBrush",
            ["TextMain"] = "TextBrush",
            ["TextMuted"] = "MutedBrush",
            ["TextSubtle"] = "SubtleBrush",
            ["BorderMain"] = "BorderMainBrush",
            ["BorderSubtle"] = "BorderSubtleBrush"
        };

    public sealed record ThemePreset(
        string Id,
        string DisplayName,
        IReadOnlyDictionary<string, WpfColor> Colors)
    {
        public override string ToString() => DisplayName;
    }

    public static IReadOnlyList<ThemePreset> Presets { get; } =
    [
        new("Dark", "Тёмная тема", Palette(
            appBg: "#141414", sidebar: "#181818", panel: "#1B1B1B", card: "#202020", cardHover: "#262626",
            input: "#181818", elevated: "#292929", accent: "#587662", accentHover: "#668A71", accentPressed: "#476050",
            accentSoft: "#202923", accentBorder: "#3A5040", text: "#F1F1F1", muted: "#B2B2B2", subtle: "#7F7F7F", icon: "#E6E6E6",
            border: "#353535", borderSubtle: "#272727", success: "#6F9878", warning: "#C49A61", danger: "#C76E6E",
            info: "#A5AAA6", purple: "#9B91AE", log: "#101010", scroll: "#454545", scrollHover: "#626262")),
        new("Light", "Светлая тема", Palette(
            appBg: "#F5F5F4", sidebar: "#EEEEEC", panel: "#FAFAF9", card: "#FFFFFF", cardHover: "#F0F2F0",
            input: "#FFFFFF", elevated: "#E9ECE9", accent: "#587662", accentHover: "#496653", accentPressed: "#3D5546",
            accentSoft: "#E7EEE9", accentBorder: "#B5C8B9", text: "#202020", muted: "#666A67", subtle: "#8C908D", icon: "#353735",
            border: "#D5D7D5", borderSubtle: "#E6E7E5", success: "#507A5A", warning: "#A66F2C", danger: "#B65353",
            info: "#6D746F", purple: "#756B88", log: "#EFEFEE", scroll: "#B8BBB8", scrollHover: "#939793"))
    ];

    public static string NormalizePresetId(string? value)
    {
        if (string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase)) return "Light";
        return "Dark";
    }

    public static ThemePreset GetPreset(string? value) =>
        Presets.First(p => p.Id == NormalizePresetId(value));

    public static void Apply(string? presetId)
    {
        var preset = GetPreset(presetId);
        var resources = WpfApplication.Current.Resources;

        foreach (var (key, color) in preset.Colors)
        {
            resources[key] = color;
            var brushKey = BrushAliases.TryGetValue(key, out var alias) ? alias : $"{key}Brush";
            SetBrush(resources, brushKey, color);
        }
    }

    private static IReadOnlyDictionary<string, WpfColor> Palette(
        string appBg, string sidebar, string panel, string card, string cardHover,
        string input, string elevated, string accent, string accentHover, string accentPressed,
        string accentSoft, string accentBorder, string text, string muted, string subtle, string icon,
        string border, string borderSubtle, string success, string warning, string danger,
        string info, string purple, string log, string scroll, string scrollHover)
    {
        var accentColor = Parse(accent);
        var successColor = Parse(success);
        var warningColor = Parse(warning);
        var dangerColor = Parse(danger);
        var infoColor = Parse(info);
        var purpleColor = Parse(purple);

        return new Dictionary<string, WpfColor>(StringComparer.Ordinal)
        {
            ["AppBg"] = Parse(appBg),
            ["SidebarBg"] = Parse(sidebar),
            ["PanelBg"] = Parse(panel),
            ["CardBg"] = Parse(card),
            ["CardHover"] = Parse(cardHover),
            ["InputBg"] = Parse(input),
            ["ElevatedBg"] = Parse(elevated),
            ["Accent"] = accentColor,
            ["AccentDark"] = Parse(accentPressed),
            ["AccentHover"] = Parse(accentHover),
            ["AccentPressed"] = Parse(accentPressed),
            ["AccentForeground"] = Colors.White,
            ["Icon"] = Parse(icon),
            ["TextMain"] = Parse(text),
            ["TextMuted"] = Parse(muted),
            ["TextSubtle"] = Parse(subtle),
            ["BorderMain"] = Parse(border),
            ["BorderSubtle"] = Parse(borderSubtle),
            ["AccentSoftBg"] = Parse(accentSoft),
            ["AccentBorder"] = Parse(accentBorder),
            ["FilterBg"] = Parse(accentSoft),
            ["FilterBorder"] = accentColor,
            ["Success"] = successColor,
            ["Warn"] = warningColor,
            ["Danger"] = dangerColor,
            ["Info"] = infoColor,
            ["Purple"] = purpleColor,
            ["SuccessSoftBg"] = Mix(successColor, Parse(appBg), 0.20),
            ["FocusWarnBg"] = Mix(warningColor, Parse(appBg), 0.18),
            ["DangerSoftBg"] = Mix(dangerColor, Parse(appBg), 0.16),
            ["InfoSoftBg"] = Mix(infoColor, Parse(appBg), 0.16),
            ["PurpleSoftBg"] = Mix(purpleColor, Parse(appBg), 0.16),
            ["TealSoftBg"] = Mix(successColor, Parse(appBg), 0.16),
            ["TealBorder"] = successColor,
            ["PrimaryButtonBg"] = accentColor,
            ["PrimaryButtonHover"] = Parse(accentHover),
            ["PrimaryButtonPressed"] = Parse(accentPressed),
            ["ActionBg"] = Parse(accentSoft),
            ["ActionSoftBg"] = Parse(accentSoft),
            ["ActionBorder"] = Parse(accentBorder),
            ["GhostButtonBg"] = Parse(panel),
            ["GhostButtonHover"] = Parse(elevated),
            ["GhostButtonPressed"] = Parse(accentSoft),
            ["GhostButtonBorder"] = Parse(border),
            ["WindowButtonBg"] = Colors.Transparent,
            ["WindowButtonBorder"] = Colors.Transparent,
            ["NavHoverBg"] = Parse(elevated),
            ["NavActiveHoverBg"] = Parse(accentSoft),
            ["ScrollThumb"] = Parse(scroll),
            ["ScrollThumbHover"] = Parse(scrollHover),
            ["SidebarFooterBg"] = Parse(panel),
            ["LogPanelBg"] = Parse(log)
        };
    }

    private static WpfColor Parse(string value) =>
        (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(value)!;

    private static WpfColor Mix(WpfColor foreground, WpfColor background, double amount)
    {
        byte Blend(byte fg, byte bg) => (byte)Math.Round(fg * amount + bg * (1 - amount));
        return WpfColor.FromRgb(
            Blend(foreground.R, background.R),
            Blend(foreground.G, background.G),
            Blend(foreground.B, background.B));
    }

    private static void SetBrush(ResourceDictionary resources, string key, WpfColor color)
    {
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }
}
