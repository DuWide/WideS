# Setup / установщик

## Быстрая сборка

```bat
setup\build-setup.bat
```

Результат:
- `setup\output\WideS-Setup.exe` — установщик для пользователей (~60 МБ, .NET внутри)
- `setup\output\WideS-Setup-portable.zip` — zip с `WideS-Setup.bat` (без Inno Setup)

## Файлы

| Путь | Назначение |
|------|------------|
| `build-setup.ps1` | publish self-contained + zip + Inno Setup |
| `WideS.iss` | скрипт Inno Setup 6 |
| `WideS-Setup/app/` | промежуточная сборка (не коммитить) |
| `WideS-Setup/WideS-Setup.ps1` | portable-установка без Inno |
| `Properties/PublishProfiles/Setup-win-x64.pubxml` | профиль dotnet publish |

## У пользователя

- Программа: `%LocalAppData%\Programs\WideS`
- Данные: `%AppData%\WideS` (не входят в setup)

Требуется Inno Setup 6 только на машине разработчика: https://jrsoftware.org/isdl.php
