using System.Windows;

namespace DevCockpit;

public partial class AiAgentEditorWindow : Window
{
    public bool Saved { get; private set; }
    public AiAgentItem Agent { get; private set; }

    public AiAgentEditorWindow(IEnumerable<string>? categories = null, AiAgentItem? source = null)
    {
        InitializeComponent();
        Agent = source is null
            ? new AiAgentItem()
            : new AiAgentItem
            {
                Id = source.Id,
                Name = source.Name,
                Url = source.Url,
                Category = string.IsNullOrWhiteSpace(source.Category) ? "AI Agents" : source.Category,
                IsPinned = source.IsPinned
            };
        foreach (var item in (categories ?? BrowserCategories()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            CategoryBox.Items.Add(item);
        }
        NameBox.Text = Agent.Name;
        UrlBox.Text = Agent.Url;
        var category = string.IsNullOrWhiteSpace(Agent.Category) ? "AI Agents" : Agent.Category;
        if (!CategoryBox.Items.Cast<object>().Any(i => string.Equals(i.ToString(), category, StringComparison.OrdinalIgnoreCase)))
        {
            CategoryBox.Items.Add(category);
        }

        CategoryBox.SelectedItem = CategoryBox.Items.Cast<object>()
            .First(i => string.Equals(i.ToString(), category, StringComparison.OrdinalIgnoreCase));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(UrlBox.Text))
        {
            System.Windows.MessageBox.Show(this, "Укажите название и ссылку.", "WideS");
            return;
        }

        if (!Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            System.Windows.MessageBox.Show(this, "Ссылка должна начинаться с http:// или https://.", "WideS");
            return;
        }

        Agent.Name = NameBox.Text.Trim();
        Agent.Url = uri.ToString();
        Agent.Category = CategoryBox.SelectedItem?.ToString() ?? "AI Agents";
        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static IEnumerable<string> BrowserCategories() => ["AI Agents", "Основное", "Mail"];
}
