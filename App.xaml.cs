using Microsoft.Toolkit.Uwp.Notifications;

namespace DevCockpit;

public partial class App : System.Windows.Application
{
    private void Application_Startup(object sender, System.Windows.StartupEventArgs e)
    {
        TaskNotificationService.Initialize();
        TaskNotificationService.RegisterApp();

        var smokeMode = Environment.GetEnvironmentVariable("WIDES_SMOKE") == "1";
        if (!smokeMode && !SingleInstanceService.TryAcquire())
        {
            SingleInstanceService.NotifyExistingInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                $"Ошибка приложения:\n{args.Exception.Message}",
                "WideS",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        AppPaths.EnsureDataDirectory();

        var settingsAlreadyExisted = System.IO.File.Exists(AppPaths.SettingsJson);
        var settingsStore = new JsonFileStore<AppSettingsData>(AppPaths.SettingsJson);
        var settings = settingsStore.Load();
        var smokeTheme = Environment.GetEnvironmentVariable("WIDES_THEME");
        ThemeService.Apply(string.IsNullOrWhiteSpace(smokeTheme) ? settings.AccentTheme : smokeTheme);
        if (settingsAlreadyExisted && !settings.IsFirstRunConfigured)
        {
            settings.IsFirstRunConfigured = true;
            settingsStore.Save(settings);
        }
        var justConfigured = false;
        if (!settings.IsFirstRunConfigured)
        {
            var firstRun = new FirstRunWindow();
            if (firstRun.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            settings.UserName = firstRun.UserNameValue;
            settings.LoginPasswordEncrypted = SecretService.Protect(firstRun.PasswordValue);
            settings.IsFirstRunConfigured = true;
            settingsStore.Save(settings);
            justConfigured = true;
        }

        if (string.IsNullOrWhiteSpace(settings.UserName))
        {
            settings.UserName = "Олег";
            settingsStore.Save(settings);
        }

        if (!justConfigured && !settings.DisableLogin &&
            !smokeMode)
        {
            var login = new LoginWindow(settings);
            if (login.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        var userName = string.IsNullOrWhiteSpace(settings.UserName) ? "Олег" : settings.UserName.Trim();
        if (!settings.DisableLogin && !smokeMode &&
            new WelcomeWindow(userName).ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        StartMainWindow();
    }

    private void StartMainWindow()
    {
        try
        {
            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось открыть главное окно:\n{ex.Message}",
                "WideS",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown();
        }
    }
}
