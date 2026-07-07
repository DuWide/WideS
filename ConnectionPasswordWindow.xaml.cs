using System.Windows;
using WpfClipboard = System.Windows.Clipboard;

namespace DevCockpit;

public partial class ConnectionPasswordWindow : Window
{
    public ConnectionPasswordWindow(string connectionName, string password)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(connectionName) ? "Пароль" : $"Пароль · {connectionName}";
        PasswordBox.Text = password;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(PasswordBox.Text))
        {
            WpfClipboard.SetText(PasswordBox.Text);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
