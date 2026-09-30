using System.Windows;
using System.Windows.Input;

namespace DevCockpit;

public partial class LoginWindow : Window
{
    private readonly AppSettingsData _settings;

    public LoginWindow(AppSettingsData settings)
    {
        InitializeComponent();
        _settings = settings;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void PasswordInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryLogin();
        }
    }

    private void Login_Click(object sender, RoutedEventArgs e) => TryLogin();

    /// <summary>Пароль был в старом формате и после входа переведён на хэш — настройки надо сохранить.</summary>
    public bool PasswordUpgraded { get; private set; }

    private void TryLogin()
    {
        if (SecretService.VerifyLogin(_settings, PasswordInput.Password, out var upgraded))
        {
            PasswordUpgraded = upgraded;
            DialogResult = true;
            return;
        }

        ErrorText.Text = "Неверный пароль";
        PasswordInput.SelectAll();
        PasswordInput.Focus();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        EditorWindowHelper.TitleBar_MouseLeftButtonDown(this, e);
}
