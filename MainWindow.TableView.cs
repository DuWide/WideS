using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;

namespace DevCockpit;

public partial class MainWindow
{
    private Grid CreateTableGrid(params GridLength[] columns)
    {
        var grid = new Grid();
        foreach (var width in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        return grid;
    }

    private Border BuildTableHeader(params (string Title, GridLength Width)[] columns)
    {
        var row = new Border
        {
            Background = (WpfBrush)FindResource("PanelBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Padding = new Thickness(10, 6, 8, 6),
            Margin = new Thickness(0)
        };

        var grid = new Grid();
        for (var i = 0; i < columns.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = columns[i].Width });
            var label = Muted(columns[i].Title);
            label.FontSize = 11;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, i);
            grid.Children.Add(label);
        }

        row.Child = grid;
        return row;
    }

    private Border WrapTableRow(Grid grid, Action? click = null)
    {
        var row = new Border
        {
            Background = (WpfBrush)FindResource("CardBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1, 0, 1, 1),
            Padding = new Thickness(8, 4, 6, 4),
            MinHeight = 34,
            Cursor = click is null ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand,
            Child = grid
        };

        if (click is not null)
        {
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (!IsInsideButton(e.OriginalSource as DependencyObject))
                {
                    click();
                }
            };
        }

        return row;
    }

    private static void AddCell(Grid grid, int column, UIElement content)
    {
        if (content is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
        }

        Grid.SetColumn(content, column);
        grid.Children.Add(content);
    }

    private StackPanel CompactRowActions(params UIElement[] items)
    {
        var panel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var item in items)
        {
            panel.Children.Add(item);
        }

        return panel;
    }

    private WpfButton CompactActionButton(string text, Action action)
    {
        var button = ActionButton(text, action);
        button.Height = 28;
        button.MinWidth = 54;
        button.Padding = new Thickness(8, 2, 8, 2);
        button.Margin = new Thickness(0, 0, 2, 0);
        return button;
    }

    private WpfButton CompactIconButton(UIElement element)
    {
        if (element is WpfButton button)
        {
            button.Width = 28;
            button.Height = 28;
            button.MinWidth = 28;
            button.Margin = new Thickness(0, 0, 2, 0);
        }

        return (WpfButton)element;
    }
}
