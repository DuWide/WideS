using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace DevCockpit;

public partial class VariablePromptWindow : Window
{
    private readonly Dictionary<string, WpfTextBox> _fields = [];

    public Dictionary<string, string> Values { get; } = [];

    public VariablePromptWindow(CommandRecipeItem recipe)
    {
        InitializeComponent();
        TitleText.Text = recipe.Name;
        CommandText.Text = recipe.Command;

        foreach (var variable in Regex.Matches(recipe.Command, "\\{([a-zA-Z0-9_а-яА-ЯёЁ-]+)\\}")
                     .Select(m => m.Groups[1].Value)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var label = new TextBlock
            {
                Text = variable,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var box = new WpfTextBox { Margin = new Thickness(0, 0, 0, 14) };
            FieldsPanel.Children.Add(label);
            FieldsPanel.Children.Add(box);
            _fields[variable] = box;
        }

        Loaded += (_, _) => _fields.Values.FirstOrDefault()?.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        foreach (var pair in _fields)
        {
            Values[pair.Key] = pair.Value.Text.Trim();
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
