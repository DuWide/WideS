# Legacy code

Код здесь **не участвует в сборке** (`DevCockpit.csproj` исключает `legacy/**`).

## winforms/

Старая WinForms-версия интерфейса до перехода на WPF (WideS).

| Файл | Назначение |
|------|------------|
| `MainForm.cs` | Главное окно WinForms |
| `NoteDialog.cs` | Редактор заметок |
| `ConnectionDialog.cs` | Редактор подключений |
| `ProjectDialog.cs` | Редактор проектов (+ `StyleTextBox` / `DarkButton`, перенесены в `WinFormsDialogHelpers.cs`) |
| `UiTheme.cs` | Общие цвета/кнопки WinForms |

Активный WinForms-диалог в основном проекте: `ContextBuilderDialog.cs` (сборка context.txt).

## installer-old/

Первая попытка portable-setup (копирование в `%LocalAppData%`). Заменена на `setup/`.

Актуальный установщик: `setup/build-setup.bat` → `setup/output/WideS-Setup.exe`.
