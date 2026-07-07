using System.Windows;
using System.Windows.Input;

namespace DevCockpit;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow(string userName)
    {
        InitializeComponent();
        WelcomeText.Text = $"Добро пожаловать, {userName}";
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
