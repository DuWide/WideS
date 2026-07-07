using System.Windows;
using System.Windows.Input;

namespace DevCockpit;

public partial class FirstRunWindow : Window
{
    public string UserNameValue { get; private set; } = "";
    public string PasswordValue { get; private set; } = "";

    public FirstRunWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UserNameBox.Focus();
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => TryContinue();

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryContinue();
        }
    }

    private void TryContinue()
    {
        if (string.IsNullOrWhiteSpace(UserNameBox.Text))
        {
            ErrorText.Text = "Укажите имя пользователя.";
            UserNameBox.Focus();
            return;
        }

        if (PasswordBox.Password != ConfirmPasswordBox.Password)
        {
            ErrorText.Text = "Пароли не совпадают.";
            ConfirmPasswordBox.SelectAll();
            ConfirmPasswordBox.Focus();
            return;
        }

        UserNameValue = UserNameBox.Text.Trim();
        PasswordValue = PasswordBox.Password;
        DialogResult = true;
    }
}
