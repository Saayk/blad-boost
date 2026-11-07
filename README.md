# Blad Boost (GameBoost)

App de Windows para mejorar el rendimiento en juegos (WPF / C#). Aplica ajustes del sistema, recomienda drivers y herramientas, y se actualiza sola.

## Compilar
Requiere Windows. Ejecuta en PowerShell:

```powershell
./build.ps1
```

## Estructura
- `App.cs`, `MainWindow.xaml` – interfaz principal
- `Core.cs`, `Services.cs` – lógica de optimización y servicios (incl. actualizaciones)
- `Lang.cs` – traducciones (`check-lang.ps1` valida las cadenas)
- `Setup.cs`, `Setup.xaml` – instalador
- `version.json` – feed de actualizaciones

## Contribuir
Haz un fork o crea una rama, y abre un Pull Request.
