# Tools

## BuildIcon.ps1

Генерация `Assets/WideS.ico` из `Assets/WideS.png`.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BuildIcon.ps1
```

Создаёт multi-size ICO (16–256 px) для exe, ярлыков и Inno Setup.

После смены PNG пересоберите иконку и проект.
