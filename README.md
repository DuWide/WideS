# WideS (DevCockpit)

Personal developer cockpit: проекты, глобальные задачи, заметки, подключения,
встроенные мессенджеры и музыка.

Основной экран WideS — «Проекты». Единая боковая панель содержит проекты, заметки,
подключения, задачи, мессенджеры, музыку и настройки. Поиск и Floating Dock доступны
в правой части заголовка окна.

Задачи отображаются таблицей и разделены на четыре вкладки: «Все», «В работе»,
«Запланировано», «Выполнено». Заметки и подключения также используют табличный вид.

Визуальная система v1.5:

- полноценные светлая и тёмная темы;
- единые токены цветов, типографики, отступов и состояний контролов;
- общие современные стили для карточек, таблиц, полей, календарей, меню, диалогов и полос прокрутки;
- спокойная иерархия действий и компактные контекстные меню в насыщенных разделах.

- **Exe:** `WideS.exe`
- **Namespace:** `DevCockpit`
- **Данные пользователя:** `%AppData%\WideS` (не в репозитории)
- **Win10+ / Win11**, x64, .NET 8

## Структура проекта

```
DevCockpit/
├── *.xaml, *.cs          # исходники WPF-приложения
├── App.xaml              # цветовые токены темы
├── Themes/               # единая дизайн-система контролов
├── MainWindow.xaml.cs    # основной UI
├── Models.cs             # JSON-модели
├── AppPaths.cs           # пути %AppData%\WideS
├── Assets/               # WideS.png, WideS.ico
├── data/projects.json    # пустой шаблон для dev (не user data)
├── Properties/           # PublishProfiles
├── setup/                # установщик → см. setup/README.md
├── tools/                # утилиты → см. tools/README.md
├── legacy/               # старый код, не в сборке → см. legacy/README.md
├── publish/              # локальный exe (gitignore)
└── bin/, obj/            # сборка (gitignore)
```

## Сборка

### Для себя (ярлык / разработка)

Нужен [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) на ПК.

```powershell
dotnet publish -c Release -o publish
```

Запуск: `publish\WideS.exe`

### Для других пользователей (setup, runtime внутри)

```bat
setup\build-setup.bat
```

Отдавать: `setup\output\WideS-Setup.exe`

## Первый запуск

При отсутствии `%AppData%\WideS\settings.json` показывается `FirstRunWindow` (имя + пароль).
Данные из репозитория и setup **не** копируются пользователю.

## Иконки

PNG → ICO: `tools\BuildIcon.ps1` (см. `tools/README.md`).

## Telegram без bot token

В настройках можно выбрать официальный `result.json`, созданный через Telegram Desktop →
Настройки → Продвинутые → Экспорт данных. WideS импортирует подходящие задачи,
следит за изменением файла и не создаёт дубли при повторном импорте.

## Полезно для AI / новых разработчиков

| Задача | Где смотреть |
|--------|----------------|
| Стили UI | `App.xaml`, `Themes/DesignSystem.xaml`, `ThemeService.cs` |
| Навигация, экраны | `MainWindow.xaml.cs`, `MainWindow.Polish.cs`, `MainWindow.*.cs` |
| Встроенные веб-приложения | `MainWindow.WebApps.cs`, `WebAppsHostView.cs`, `WebAppView.cs` |
| Установщик | `setup/README.md`, `setup/WideS.iss` |
| Telegram-импорт задач | `TelegramTaskService.cs`, `TelegramTaskParser.cs` |
| Context builder (WinForms) | `ContextBuilderDialog.cs` |
| Старый WinForms UI | `legacy/winforms/` |
| Старый installer | `legacy/installer-old/` |

## Версия

Задаётся в `DevCockpit.csproj` (`<Version>`) и `setup/WideS.iss` (`MyAppVersion`).

## Git / GitHub

Репозиторий: исходники в git, пользовательские данные (`%AppData%\WideS`) и артефакты сборки — в `.gitignore`.

```powershell
# Сборка
dotnet publish -c Release -o publish

# Первый push (после gh auth login)
git remote add origin https://github.com/<user>/<repo>.git
git push -u origin main
```

Авторизация GitHub CLI: `gh auth login` → https://github.com/login/device
