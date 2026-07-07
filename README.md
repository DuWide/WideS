# WideS (DevCockpit)

Personal developer cockpit: проекты, задачи, заметки, подключения, команды, DropZone.

- **Exe:** `WideS.exe`
- **Namespace:** `DevCockpit`
- **Данные пользователя:** `%AppData%\WideS` (не в репозитории)
- **Win10+ / Win11**, x64, .NET 8

## Структура проекта

```
DevCockpit/
├── *.xaml, *.cs          # исходники WPF-приложения
├── App.xaml              # глобальные стили (DarkComboBox, кнопки)
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
├── bin/, obj/            # сборка (gitignore)
└── _DevCockpitBackups/   # локальные бэкапы (gitignore)
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

## Полезно для AI / новых разработчиков

| Задача | Где смотреть |
|--------|----------------|
| Стили UI | `App.xaml` |
| Навигация, экраны | `MainWindow.xaml.cs`, `MainWindow.Polish.cs` |
| Установщик | `setup/README.md`, `setup/WideS.iss` |
| Telegram-импорт задач | `TelegramTaskService.cs`, `TelegramTaskParser.cs` |
| Context builder (WinForms) | `ContextBuilderDialog.cs` |
| Старый WinForms UI | `legacy/winforms/` |
| Старый installer | `legacy/installer-old/` |

## Версия

Задаётся в `DevCockpit.csproj` (`<Version>`) и `setup/WideS.iss` (`MyAppVersion`).
