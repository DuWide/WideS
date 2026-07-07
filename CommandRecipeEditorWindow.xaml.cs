using System.Windows;

namespace DevCockpit;

public partial class CommandRecipeEditorWindow : Window
{
    public bool Saved { get; private set; }
    public CommandRecipeItem Recipe { get; private set; }

    public CommandRecipeEditorWindow(CommandRecipeItem? source = null)
    {
        InitializeComponent();
        Recipe = source is null
            ? new CommandRecipeItem()
            : new CommandRecipeItem { Id = source.Id, Name = source.Name, Command = source.Command, UseShell = source.UseShell };
        NameBox.Text = Recipe.Name;
        CommandBox.Text = Recipe.Command;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(CommandBox.Text))
        {
            System.Windows.MessageBox.Show(this, "Укажите название и команду.", "WideS");
            return;
        }

        Recipe.Name = NameBox.Text.Trim();
        Recipe.Command = CommandBox.Text.Trim();
        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
