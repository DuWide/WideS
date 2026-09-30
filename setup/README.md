# Setup / установщик

## Быстрая сборка

```bat
setup\build-setup.bat
```

Результат:
- `setup\output\WideS-Setup.exe` — установщик для пользователей (~60 МБ, .NET внутри)

## Обновление на другом ПК

1. Закрой WideS (если запущен).
2. Запусти новый `WideS-Setup.exe` поверх старой установки.
3. Мастер определит существующую программу и обновит её в `%LocalAppData%\Programs\WideS`.
4. Данные в `%AppData%\WideS` не трогаются.

Удалять старую версию вручную не нужно — тот же `AppId`, установка идёт поверх.

## Файлы

| Путь | Назначение |
|------|------------|
| `build-setup.ps1` | publish self-contained + Inno Setup |
| `WideS.iss` | скрипт Inno Setup 6 |
| `WideS-Setup/app/` | промежуточная сборка (не коммитить) |
| `Properties/PublishProfiles/Setup-win-x64.pubxml` | профиль dotnet publish |

## У пользователя

- Программа: `%LocalAppData%\Programs\WideS`
- Данные: `%AppData%\WideS` (не входят в setup)

Требуется Inno Setup 6 только на машине разработчика: https://jrsoftware.org/isdl.php
