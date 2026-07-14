using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DevCockpit;

public static class UiIconFactory
{
    public static FrameworkElement Create(string iconName, double size)
    {
        var glyph = iconName switch
        {
            "edit" => 0xE70F, "grid" => 0xE80A, "table" => 0xE8A9, "list" => 0xEA37,
            "back" => 0xE72B, "forward" => 0xE72A, "close" => 0xE8BB, "minus" => 0xE921,
            "maximize" => 0xE922, "restore" => 0xE923, "star" => 0xE734, "delete" => 0xE74D,
            "play" => 0xE768, "pause" => 0xE769, "prev" => 0xE892, "next" => 0xE893,
            "music" => 0xE8D6, "nav-home" => 0xE80F, "nav-projects" => 0xE8B7,
            "nav-tasks" or "task" => 0xE73E,
            "nav-notes" or "note" => 0xE70B,
            "nav-connections" or "connection" => 0xE71B,
            "nav-contacts" => 0xE716, "nav-browser" => 0xE774, "nav-commands" => 0xE756,
            "nav-backup" => 0xE896,
            "nav-dropzone" or "drop" => 0xE898,
            "nav-settings" => 0xE713, "nav-clipboard" => 0xE77F, "nav-pulse" => 0xE9D9,
            "copy" => 0xE8C8, "cpu" => 0xE950, "memory" => 0xE7F8, "disk" => 0xEDA2,
            "more" => 0xE712, "search" => 0xE721,
            "dock" or "open" => 0xE7F4,
            _ => 0xE10C
        };

        var icon = new TextBlock
        {
            Text = char.ConvertFromUtf32(glyph),
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 20,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "IconBrush");

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = icon
        };
    }
}
