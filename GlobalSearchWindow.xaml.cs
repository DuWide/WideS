using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DevCockpit;

public partial class GlobalSearchWindow : Window
{
    public sealed record SearchHit(string Kind, string Title, string Subtitle, Action Open);

    private readonly Func<string, IReadOnlyList<SearchHit>> _search;
    private IReadOnlyList<SearchHit> _hits = [];

    public GlobalSearchWindow(Func<string, IReadOnlyList<SearchHit>> search)
    {
        InitializeComponent();
        _search = search;
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };
    }

    private void RenderResults()
    {
        ResultsList.Items.Clear();
        foreach (var hit in _hits)
        {
            var panel = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
            panel.Children.Add(new TextBlock
            {
                Text = $"{hit.Kind}: {hit.Title}",
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(hit.Subtitle))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = hit.Subtitle,
                    Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            var item = new ListBoxItem { Content = panel, Tag = hit, Padding = new Thickness(4) };
            ResultsList.Items.Add(item);
        }

        if (ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
        }
    }

    private void OpenSelected()
    {
        if (ResultsList.SelectedItem is not ListBoxItem { Tag: SearchHit hit })
        {
            return;
        }

        DialogResult = true;
        Close();
        hit.Open();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _hits = _search(SearchBox.Text.Trim());
        RenderResults();
    }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (ResultsList.Items.Count > 0)
            {
                ResultsList.Focus();
                ResultsList.SelectedIndex = 0;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OpenSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private void ResultsList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelected();
    }
}
