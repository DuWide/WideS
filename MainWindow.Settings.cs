using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using Forms = System.Windows.Forms;

namespace DevCockpit;

public partial class MainWindow
{
    private void ShowSettings()
    {
        EnterView("settings");
        SetTitle("Настройки", "Вход, локальные пути и параметры");

        var panel = new WrapPanel();

        var accountCard = Card("WideS");
        accountCard.Width = 720;
        var account = BaseCardStack("WideS");
        account.Children.Add(Muted("Имя для приветствия"));
        var userName = new WpfTextBox { Text = _settings.UserName, Margin = new Thickness(0, 8, 0, 12), MinWidth = 520 };
        account.Children.Add(userName);
        account.Children.Add(Muted("Новый пароль входа. Оставьте пустым, если пароль менять не нужно."));
        var loginPassword = new PasswordBox
        {
            Margin = new Thickness(0, 8, 0, 8),
            MinWidth = 520,
            Padding = new Thickness(10),
            Background = (WpfBrush)FindResource("PanelBrush"),
            Foreground = (WpfBrush)FindResource("TextBrush")
        };
        account.Children.Add(loginPassword);
        account.Children.Add(Muted("Подтвердите новый пароль"));
        var confirmPassword = new PasswordBox
        {
            Margin = new Thickness(0, 8, 0, 8),
            MinWidth = 520,
            Padding = new Thickness(10),
            Background = (WpfBrush)FindResource("PanelBrush"),
            Foreground = (WpfBrush)FindResource("TextBrush")
        };
        account.Children.Add(confirmPassword);
        var disableLogin = new System.Windows.Controls.CheckBox
        {
            Content = "Отключить авторизацию",
            IsChecked = _settings.DisableLogin,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            Margin = new Thickness(0, 4, 0, 12)
        };
        account.Children.Add(disableLogin);
        account.Children.Add(ActionButton("Сохранить вход", () =>
        {
            _settings.UserName = string.IsNullOrWhiteSpace(userName.Text) ? "Олег" : userName.Text.Trim();
            _settings.DisableLogin = disableLogin.IsChecked == true;
            if (!string.IsNullOrEmpty(loginPassword.Password))
            {
                if (loginPassword.Password != confirmPassword.Password)
                {
                    WpfMessageBox.Show(this, "Пароли не совпадают.", "WideS");
                    return;
                }

                _settings.LoginPasswordEncrypted = SecretService.Protect(loginPassword.Password);
                loginPassword.Clear();
                confirmPassword.Clear();
            }
            _settingsStore.Save(_settings);
            AddLog("OK", "Настройки входа сохранены.");
            ShowHome();
        }));
        accountCard.Child = account;
        panel.Children.Add(accountCard);

        var card = Card("AnyDesk");
        card.Width = 720;
        var stack = BaseCardStack("AnyDesk");
        stack.Children.Add(Muted("Путь к AnyDesk.exe"));
        var path = new WpfTextBox { Text = _settings.AnyDeskPath, Margin = new Thickness(0, 8, 0, 8), MinWidth = 520 };
        stack.Children.Add(path);
        var buttons = new WrapPanel();
        buttons.Children.Add(ActionButton("Выбрать", () =>
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog { Filter = "AnyDesk.exe|AnyDesk.exe|Все файлы|*.*" };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) path.Text = dialog.FileName;
        }, false));
        buttons.Children.Add(ActionButton("Сохранить", () =>
        {
            _settings.AnyDeskPath = path.Text.Trim();
            _settingsStore.Save(_settings);
            AddLog("OK", "Настройки сохранены.");
        }));
        stack.Children.Add(buttons);
        stack.Children.Add(Muted($"Данные хранятся здесь: {AppPaths.DataDirectory}"));
        card.Child = stack;
        panel.Children.Add(card);

        var yummyCard = Card("YummyAnime");
        yummyCard.Width = 720;
        var yummyStack = BaseCardStack("YummyAnime");
        yummyStack.Children.Add(Muted("Публичный X-Application token приложения. Приватный токен сюда не вводите."));
        var yummyToken = new PasswordBox
        {
            Margin = new Thickness(0, 8, 0, 8),
            MinWidth = 520,
            Padding = new Thickness(10),
            Background = (WpfBrush)FindResource("PanelBrush"),
            Foreground = (WpfBrush)FindResource("TextBrush")
        };
        if (!string.IsNullOrWhiteSpace(SecretService.Unprotect(_settings.YummyAnimeAppTokenEncrypted)))
        {
            yummyToken.Password = "********";
        }
        yummyStack.Children.Add(yummyToken);
        yummyStack.Children.Add(Muted("Логин пользователя выполняется в разделе «Плеер». Пароль не сохраняется; токен аккаунта защищён DPAPI."));
        var yummyButtons = new WrapPanel();
        yummyButtons.Children.Add(ActionButton("Сохранить токен", () =>
        {
            if (!string.IsNullOrWhiteSpace(yummyToken.Password) && yummyToken.Password != "********")
            {
                _settings.YummyAnimeAppTokenEncrypted = SecretService.Protect(yummyToken.Password.Trim());
                _settings.YummyAnimeUserTokenEncrypted = "";
            }
            _settingsStore.Save(_settings);
            ResetVideoBrowser();
            AddLog("OK", "Токен YummyAnime сохранён.");
        }, false));
        yummyButtons.Children.Add(ActionButton("Открыть приложения YummyAnime", () =>
        {
            Process.Start(new ProcessStartInfo("https://yummyani.me/dev/applications")
            {
                UseShellExecute = true
            });
        }, false));
        yummyStack.Children.Add(yummyButtons);
        yummyCard.Child = yummyStack;
        panel.Children.Add(yummyCard);

        var hotkeys = Card("Горячие клавиши");
        hotkeys.Width = 720;
        var hotkeysStack = BaseCardStack("Горячие клавиши");
        hotkeysStack.Children.Add(Text("Alt+F1 — новая заметка\nAlt+F2 — новая задача\nCtrl+K — поиск\nCtrl+Alt+Space — Floating Dock", 13, (WpfBrush)FindResource("MutedBrush"), new Thickness()));
        var clipboardScreenshotCheck = new System.Windows.Controls.CheckBox
        {
            Content = "Предлагать сохранить скриншот из буфера",
            IsChecked = _settings.ClipboardScreenshotPrompt,
            Margin = new Thickness(0, 12, 0, 4),
            Foreground = (WpfBrush)FindResource("TextBrush")
        };
        hotkeysStack.Children.Add(clipboardScreenshotCheck);
        var clipboardHistoryCheck = new System.Windows.Controls.CheckBox
        {
            Content = "Вести локальную историю буфера",
            IsChecked = _settings.ClipboardHistoryEnabled,
            Margin = new Thickness(0, 4, 0, 4),
            Foreground = (WpfBrush)FindResource("TextBrush")
        };
        hotkeysStack.Children.Add(clipboardHistoryCheck);
        hotkeysStack.Children.Add(Muted("Текст хранится через DPAPI; потенциальные пароли скрываются в превью."));
        hotkeysStack.Children.Add(Muted("Если при RDP не работает копирование/вставка — снимите галочку и сохраните."));
        hotkeysStack.Children.Add(ActionButton("Сохранить буфер", () =>
        {
            _settings.ClipboardScreenshotPrompt = clipboardScreenshotCheck.IsChecked == true;
            _settings.ClipboardHistoryEnabled = clipboardHistoryCheck.IsChecked == true;
            _settingsStore.Save(_settings);
            UpdateClipboardScreenshotListener();
            AddLog("OK", "Настройка буфера обмена сохранена.");
        }, false));
        hotkeys.Child = hotkeysStack;
        panel.Children.Add(hotkeys);

        var appearance = Card("Внешний вид");
        appearance.Width = 720;
        var appearanceStack = BaseCardStack("Внешний вид");
        appearanceStack.Children.Add(Muted("Тема интерфейса"));
        var accentBox = UiHelpers.CreateToolbarComboBox(320);
        foreach (var preset in ThemeService.Presets) accentBox.Items.Add(preset);
        accentBox.SelectedItem = ThemeService.GetPreset(_settings.AccentTheme);
        accentBox.SelectionChanged += (_, _) =>
        {
            if (accentBox.SelectedItem is ThemeService.ThemePreset preset)
            {
                ApplyTheme(preset.Id);
            }
        };
        appearanceStack.Children.Add(accentBox);
        var compactSidebarCheck = new System.Windows.Controls.CheckBox { Content = "Компактная боковая панель", IsChecked = _settings.CompactSidebar, Margin = new Thickness(0, 8, 0, 12) };
        appearanceStack.Children.Add(compactSidebarCheck);
        appearanceStack.Children.Add(Muted("Режим интерфейса"));
        var modeBox = UiHelpers.CreateToolbarComboBox(320);
        foreach (var option in WorkModeService.ModeOptions())
        {
            modeBox.Items.Add(new SelectOption(option.Label, option.Value));
        }
        modeBox.SelectedItem = modeBox.Items.OfType<SelectOption>().FirstOrDefault(x => x.Value == _settings.WorkMode) ?? modeBox.Items[0];
        appearanceStack.Children.Add(modeBox);
        var modeHint = Muted(WorkModeService.DescribeEffects(_settings.WorkMode));
        modeBox.SelectionChanged += (_, _) =>
        {
            if (modeBox.SelectedItem is SelectOption option) modeHint.Text = WorkModeService.DescribeEffects(option.Value);
        };
        appearanceStack.Children.Add(modeHint);
        appearanceStack.Children.Add(ActionButton("Применить оформление", () =>
        {
            _settings.AccentTheme = (accentBox.SelectedItem as ThemeService.ThemePreset)?.Id ?? "Dark";
            _settings.WorkMode = (modeBox.SelectedItem as SelectOption)?.Value ?? "Work";
            _settings.CompactSidebar = compactSidebarCheck.IsChecked == true;
            _settingsStore.Save(_settings);
            ApplyTheme(_settings.AccentTheme);
            ApplyCompactSidebar(_settings.CompactSidebar);
            _ = UpdateNowPlaying();
            AddLog("OK", "Настройки вида сохранены.");
            RefreshCurrentView();
        }));
        appearance.Child = appearanceStack;
        panel.Children.Add(appearance);

        var dockCard = Card("Floating Dock");
        dockCard.Width = 720;
        var dockStack = BaseCardStack("Floating Dock");
        dockStack.Children.Add(Muted("Позиция"));
        var dockPos = new System.Windows.Controls.ComboBox { MinWidth = 220, Margin = new Thickness(0, 8, 0, 8), IsEditable = false };
        dockPos.Items.Add("Center"); dockPos.Items.Add("Tray");
        dockPos.SelectedItem = _settings.DockPosition;
        dockStack.Children.Add(dockPos);
        var dockAutoHideCheck = new System.Windows.Controls.CheckBox { Content = "Автоскрытие при потере фокуса", IsChecked = _settings.DockAutoHide, Margin = new Thickness(0, 4, 0, 8) };
        dockStack.Children.Add(dockAutoHideCheck);
        dockStack.Children.Add(ActionButton("Сохранить Dock", () =>
        {
            _settings.DockPosition = dockPos.SelectedItem?.ToString() ?? "Center";
            _settings.DockAutoHide = dockAutoHideCheck.IsChecked == true;
            _settingsStore.Save(_settings);
            AddLog("OK", "Настройки Dock сохранены.");
        }, false));
        dockCard.Child = dockStack;
        panel.Children.Add(dockCard);

        var startupCard = Card("Автозапуск");
        startupCard.Width = 720;
        var startupStack = BaseCardStack("Автозапуск");
        var runAtStartup = new System.Windows.Controls.CheckBox { Content = "Запускать WideS с Windows", IsChecked = _settings.RunAtStartup || StartupService.IsRunAtStartup(), Margin = new Thickness(0, 0, 0, 8) };
        startupStack.Children.Add(runAtStartup);
        startupStack.Children.Add(ActionButton("Сохранить автозапуск", () =>
        {
            _settings.RunAtStartup = runAtStartup.IsChecked == true;
            StartupService.SetRunAtStartup(_settings.RunAtStartup, Environment.ProcessPath ?? "");
            _settingsStore.Save(_settings);
            AddLog("OK", "Автозапуск обновлён.");
        }, false));
        startupCard.Child = startupStack;
        panel.Children.Add(startupCard);

        var telegramCard = Card("Telegram");
        telegramCard.Width = 720;
        var telegramStack = BaseCardStack("Telegram");
        telegramStack.Children.Add(Muted("Импорт задач со статусом «В работе». Режим экспорта Telegram Desktop работает без bot token."));
        var telegramEnabled = new System.Windows.Controls.CheckBox
        {
            Content = "Следить за обновлением источника",
            IsChecked = _settings.TelegramEnabled,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            Margin = new Thickness(0, 4, 0, 8)
        };
        telegramStack.Children.Add(telegramEnabled);

        telegramStack.Children.Add(Muted("Источник задач"));
        var telegramSource = UiHelpers.CreateToolbarComboBox(360);
        telegramSource.Items.Add(new SelectOption("Экспорт Telegram Desktop — без токена", "DesktopExport"));
        telegramSource.Items.Add(new SelectOption("Telegram Bot API", "BotApi"));
        telegramSource.SelectedItem = telegramSource.Items.OfType<SelectOption>()
            .FirstOrDefault(x => x.Value.Equals(_settings.TelegramSource, StringComparison.OrdinalIgnoreCase))
            ?? telegramSource.Items[0];
        telegramStack.Children.Add(telegramSource);
        PasswordBox telegramToken = null!;

        var desktopSection = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        desktopSection.Children.Add(Muted("Файл result.json из Telegram Desktop → Настройки → Продвинутые → Экспорт данных."));
        var telegramPath = new WpfTextBox
        {
            Text = _settings.TelegramDesktopExportPath,
            Margin = new Thickness(0, 8, 0, 8),
            MinWidth = 520
        };
        desktopSection.Children.Add(telegramPath);
        var desktopButtons = new WrapPanel();
        desktopButtons.Children.Add(ActionButton("Выбрать result.json", () =>
        {
            using var dialog = new Forms.OpenFileDialog
            {
                Filter = "Экспорт Telegram (result.json)|result.json|JSON-файлы|*.json",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) telegramPath.Text = dialog.FileName;
        }, false));
        desktopButtons.Children.Add(ActionButton("Проверить и импортировать", async () =>
        {
            SaveTelegramSettings();
            await PollTelegramTasksAsync(force: true, showSummary: true);
        }, false));
        desktopSection.Children.Add(desktopButtons);
        desktopSection.Children.Add(Muted("WideS не читает закрытую папку tdata. После нового экспорта обновите result.json; дубли не создаются."));
        telegramStack.Children.Add(desktopSection);

        var botSection = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        botSection.Children.Add(Muted("Bot Token от BotFather"));
        telegramToken = new PasswordBox
        {
            Margin = new Thickness(0, 8, 0, 8),
            MinWidth = 520,
            Padding = new Thickness(10),
            Background = (WpfBrush)FindResource("PanelBrush"),
            Foreground = (WpfBrush)FindResource("TextBrush")
        };
        if (!string.IsNullOrWhiteSpace(SecretService.Unprotect(_settings.TelegramBotTokenEncrypted)))
        {
            telegramToken.Password = "********";
        }
        botSection.Children.Add(telegramToken);
        botSection.Children.Add(Muted($"Ожидаемый Bot ID: {_settings.TelegramBotId}"));
        botSection.Children.Add(ActionButton("Проверить Bot API", async () =>
        {
            SaveTelegramSettings();
            await PollTelegramTasksAsync(force: true, showSummary: true);
        }, false));
        telegramStack.Children.Add(botSection);

        void SaveTelegramSettings()
        {
            _settings.TelegramEnabled = telegramEnabled.IsChecked == true;
            _settings.TelegramSource = (telegramSource.SelectedItem as SelectOption)?.Value ?? "DesktopExport";
            _settings.TelegramDesktopExportPath = telegramPath.Text.Trim();
            if (!string.IsNullOrWhiteSpace(telegramToken.Password) && telegramToken.Password != "********")
            {
                _settings.TelegramBotTokenEncrypted = SecretService.Protect(telegramToken.Password.Trim());
            }
            _settingsStore.Save(_settings);
            RestartTelegramPolling();
        }

        void UpdateTelegramSourceVisibility()
        {
            var desktop = (telegramSource.SelectedItem as SelectOption)?.Value != "BotApi";
            desktopSection.Visibility = desktop ? Visibility.Visible : Visibility.Collapsed;
            botSection.Visibility = desktop ? Visibility.Collapsed : Visibility.Visible;
        }
        telegramSource.SelectionChanged += (_, _) => UpdateTelegramSourceVisibility();
        UpdateTelegramSourceVisibility();

        var telegramButtons = new WrapPanel();
        telegramButtons.Children.Add(ActionButton("Сохранить настройки", () =>
        {
            SaveTelegramSettings();
            AddLog("OK", "Настройки Telegram сохранены.");
        }, false));
        telegramStack.Children.Add(telegramButtons);
        telegramCard.Child = telegramStack;
        panel.Children.Add(telegramCard);

        var notifyCard = Card("Уведомления");
        notifyCard.Width = 720;
        var notifyStack = BaseCardStack("Уведомления");
        var toastEnabled = new System.Windows.Controls.CheckBox
        {
            Content = "Toast-уведомления для напоминаний задач",
            IsChecked = _settings.ToastNotificationsEnabled,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        };
        notifyStack.Children.Add(toastEnabled);
        notifyStack.Children.Add(Muted("Toast показывается, когда WideS свёрнут или не в фокусе."));
        notifyStack.Children.Add(ActionButton("Сохранить", () =>
        {
            _settings.ToastNotificationsEnabled = toastEnabled.IsChecked == true;
            _settingsStore.Save(_settings);
            AddLog("OK", "Настройки уведомлений сохранены.");
        }));
        notifyCard.Child = notifyStack;
        panel.Children.Add(notifyCard);

        var portalCard = Card("Локальный портал");
        portalCard.Width = 720;
        var portalStack = BaseCardStack("Локальный портал");
        var portalEnabled = new System.Windows.Controls.CheckBox
        {
            Content = "Включить веб-портал",
            IsChecked = _settings.PortalEnabled,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        portalStack.Children.Add(portalEnabled);
        portalStack.Children.Add(Muted("1) Поставьте галочку и нажмите «Сохранить портал»."));
        portalStack.Children.Add(Muted("2) Нажмите «Открыть в браузере» — должна открыться страница http://127.0.0.1:7788"));
        portalStack.Children.Add(Muted("3) С телефона/другого ПК: тот же Wi‑Fi, адрес http://ВАШ_IP:7788 (IP покажется в логе после сохранения)."));
        var portalPort = new WpfTextBox
        {
            Text = _settings.PortalPort.ToString(),
            MinWidth = 120,
            Width = 160,
            Margin = new Thickness(0, 8, 0, 8)
        };
        portalStack.Children.Add(Muted("Порт"));
        portalStack.Children.Add(portalPort);
        if (_portalService.IsRunning)
        {
            portalStack.Children.Add(Muted($"Работает: {string.Join(" | ", _portalService.BoundUrls)}"));
        }
        else if (!string.IsNullOrWhiteSpace(_portalService.LastError))
        {
            portalStack.Children.Add(Muted($"Ошибка: {_portalService.LastError}"));
        }

        var portalButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        portalButtons.Children.Add(ActionButton("Сохранить портал", () =>
        {
            _settings.PortalEnabled = portalEnabled.IsChecked == true;
            if (int.TryParse(portalPort.Text.Trim(), out var port) && port is > 0 and < 65536)
            {
                _settings.PortalPort = port;
            }
            _settingsStore.Save(_settings);
            RestartPortal();
            if (_settings.PortalEnabled && _portalService.IsRunning)
            {
                Copy(PrimaryPortalUrl(), "Ссылка портала скопирована.");
            }
            AddLog("OK", _settings.PortalEnabled ? "Портал перезапущен." : "Портал остановлен.");
            ShowSettings();
        }));
        portalButtons.Children.Add(ActionButton("Открыть в браузере", OpenPortalInBrowser, false));
        portalButtons.Children.Add(ActionButton("Копировать ссылку", () =>
        {
            if (!_portalService.IsRunning)
            {
                WpfMessageBox.Show(this, "Сначала включите и сохраните портал.", "WideS");
                return;
            }
            Copy(PrimaryPortalUrl(), "Ссылка портала скопирована.");
        }, false));
        portalStack.Children.Add(portalButtons);
        portalCard.Child = portalStack;
        panel.Children.Add(portalCard);

        var dataCard = Card("Данные");
        dataCard.Width = 720;
        var dataStack = BaseCardStack("Данные");
        dataStack.Children.Add(Muted($"Папка данных: {AppPaths.DataDirectory}"));
        var dataButtons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        dataButtons.Children.Add(ActionButton("Экспорт ZIP", ExportData, false));
        dataStack.Children.Add(dataButtons);
        dataCard.Child = dataStack;
        panel.Children.Add(dataCard);

        ContentHost.Content = panel;
    }
}
