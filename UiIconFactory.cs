using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfPath = System.Windows.Shapes.Path;
using Viewbox = System.Windows.Controls.Viewbox;

namespace DevCockpit;

public static class UiIconFactory
{
    private const double Canvas = 24;
    private const double StrokeWidth = 1.7;

    private sealed record IconShape(string Data, bool Filled = false);

    private static readonly Dictionary<string, IconShape> Icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["close"] = new("M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5"),
        ["minus"] = new("M5 12 H19"),
        ["maximize"] = new("M5.2 5.2 H18.8 V18.8 H5.2 Z"),
        ["restore"] = new("M8.4 8.4 H18.8 V18.8 H8.4 Z M5.2 15.6 V5.2 H15.6"),
        ["back"] = new("M14.8 5.4 L8 12 L14.8 18.6"),
        ["forward"] = new("M9.2 5.4 L16 12 L9.2 18.6"),

        ["search"] = new("M4.5 10.8 A6.3 6.3 0 1 0 17.1 10.8 A6.3 6.3 0 1 0 4.5 10.8 M15.6 15.6 L20 20"),
        ["reload"] = new("M19 12 A7 7 0 1 1 16.6 6.7 M19.4 3.4 V7.9 H14.9"),
        ["copy"] = new("M8.6 8.6 H19.4 V19.4 H8.6 Z M15.4 8.6 V4.6 H4.6 V15.4 H8.6"),
        ["edit"] = new("M4.5 19.5 V15.6 L15.9 4.2 L19.8 8.1 L8.4 19.5 Z M14.2 5.9 L18.1 9.8"),
        ["delete"] = new("M4.5 6.9 H19.5 M9.3 6.9 V4.6 H14.7 V6.9 M6.6 6.9 L7.5 19.6 H16.5 L17.4 6.9 M10.2 10.4 V16.4 M13.8 10.4 V16.4"),
        ["more"] = new(
            "M12 4.3 A1.65 1.65 0 1 0 12 7.6 A1.65 1.65 0 1 0 12 4.3 Z " +
            "M12 10.35 A1.65 1.65 0 1 0 12 13.65 A1.65 1.65 0 1 0 12 10.35 Z " +
            "M12 16.4 A1.65 1.65 0 1 0 12 19.7 A1.65 1.65 0 1 0 12 16.4 Z",
            Filled: true),

        ["grid"] = new("M4.4 4.4 H10.7 V10.7 H4.4 Z M13.3 4.4 H19.6 V10.7 H13.3 Z M4.4 13.3 H10.7 V19.6 H4.4 Z M13.3 13.3 H19.6 V19.6 H13.3 Z"),
        ["table"] = new("M3.8 5 H20.2 V19 H3.8 Z M3.8 9.6 H20.2 M9.3 9.6 V19 M14.8 9.6 V19"),
        ["list"] = new("M4.4 6.6 H19.6 M4.4 12 H19.6 M4.4 17.4 H19.6"),
        ["star"] = new("M12 3.6 L14.6 9 L20.5 9.8 L16.2 14 L17.3 19.9 L12 17.1 L6.7 19.9 L7.8 14 L3.5 9.8 L9.4 9 Z"),

        ["play"] = new("M8.2 5.2 L18.8 12 L8.2 18.8 Z", Filled: true),
        ["pause"] = new("M8.4 5.2 H11.2 V18.8 H8.4 Z M12.8 5.2 H15.6 V18.8 H12.8 Z", Filled: true),
        ["prev"] = new("M17.8 5.4 L9.4 12 L17.8 18.6 Z M6.4 5.4 H8 V18.6 H6.4 Z", Filled: true),
        ["next"] = new("M6.2 5.4 L14.6 12 L6.2 18.6 Z M16 5.4 H17.6 V18.6 H16 Z", Filled: true),
        ["music"] = new(
            "M6.4 15.9 A2.6 2.6 0 1 0 11.6 15.9 A2.6 2.6 0 1 0 6.4 15.9 " +
            "M11.6 15.9 V5.2 L19.8 3.4 V13.7 " +
            "M14.6 13.7 A2.6 2.6 0 1 0 19.8 13.7 A2.6 2.6 0 1 0 14.6 13.7"),

        ["home"] = new("M4 11 L12 4.2 L20 11 V19.8 H4 Z M9.8 19.8 V14 H14.2 V19.8"),
        ["nav-projects"] = new("M3.6 5.8 H9.8 L12 8.4 H20.4 V18.8 H3.6 Z"),
        ["nav-notes"] = new("M6.2 3.8 H14.1 L18.4 8.1 V20.2 H6.2 Z M14.1 3.8 V8.1 H18.4 M9.2 12.6 H15.4 M9.2 16.2 H15.4"),
        ["nav-connections"] = new("M3.6 5.2 H20.4 V15.8 H3.6 Z M9.2 19.6 H14.8 M12 15.8 V19.6"),
        ["nav-tasks"] = new("M4.2 7.2 L6.2 9.2 L9.6 5.6 M4.2 16.4 L6.2 18.4 L9.6 14.8 M12.4 7.2 H19.8 M12.4 16.4 H19.8"),
        ["nav-messengers"] = new("M4 5.6 H20 V16 H12.4 L8 19.6 V16 H4 Z"),
        ["nav-settings"] = new(
            "M3.8 8 H20.2 M3.8 16 H20.2 " +
            "M7.6 8 A2.3 2.3 0 1 0 12.2 8 A2.3 2.3 0 1 0 7.6 8 " +
            "M11.8 16 A2.3 2.3 0 1 0 16.4 16 A2.3 2.3 0 1 0 11.8 16"),

        ["dock"] = new("M3.6 5.4 H20.4 V18.6 H3.6 Z M3.6 14.2 H20.4 M8.4 16.4 H15.6"),
        ["open"] = new("M13.8 4.4 H19.6 V10.2 M19.6 4.4 L11.4 12.6 M17.6 14.2 V19.6 H4.4 V6.4 H9.8")
    };

    public static FrameworkElement Create(string iconName, double size)
    {
        var shape = Resolve(iconName);
        var path = new WpfPath
        {
            Data = Geometry.Parse(shape.Data),
            Width = Canvas,
            Height = Canvas,
            Stretch = Stretch.None,
            SnapsToDevicePixels = true
        };

        if (shape.Filled)
        {
            path.SetResourceReference(Shape.FillProperty, "IconBrush");
        }
        else
        {
            path.SetResourceReference(Shape.StrokeProperty, "IconBrush");
            path.StrokeThickness = StrokeWidth;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
        }

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = path
        };
    }

    private static IconShape Resolve(string iconName)
    {
        if (Icons.TryGetValue(iconName, out var shape))
        {
            return shape;
        }

        var alias = iconName switch
        {
            "task" => "nav-tasks",
            "note" => "nav-notes",
            "connection" => "nav-connections",
            "settings" => "nav-settings",
            "projects" => "nav-projects",
            "messengers" => "nav-messengers",
            _ => "nav-projects"
        };

        return Icons[alias];
    }
}
