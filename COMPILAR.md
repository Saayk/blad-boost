# Compilar y publicar Blad Boost (guía para Windows)

La app es WPF / .NET Framework 4 y **solo se compila en Windows**. No hace falta instalar Visual Studio: se usa el compilador `csc.exe` que ya trae Windows (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319`).

## Requisitos
- Windows 10 u 11.
- [Git](https://git-scm.com/download/win).
- (Opcional) [GitHub CLI](https://cli.github.com/) con `gh auth login`, para publicar el release con un solo comando.

## Pasos
1. Clona o actualiza el repo:
   ```powershell
   git clone https://github.com/Saayk/blad-boost.git
   cd blad-boost
   git pull origin main
   ```
2. Compila y publica el release (versión indicada en `version.json`):
   ```powershell
   powershell -ExecutionPolicy Bypass -File publicar-release.ps1
   ```
   Solo compilar, sin publicar: `./build.ps1`. Debe terminar con `BUILD OK 1.0.0`.
3. Resultado en la carpeta `dist`: `GameBoost.exe`, `GameBoost_Setup.exe` (el instalador) y `version.json`.

## Si no tienes `gh`
Sube `dist\GameBoost_Setup.exe` en https://github.com/Saayk/blad-boost/releases/new con el tag `v1.0.0`.
El archivo **debe llamarse exactamente `GameBoost_Setup.exe`**, porque la app descarga `releases/latest/download/GameBoost_Setup.exe` para actualizarse.

## Cómo llegan las actualizaciones a quien ya lo tiene instalado
La app lee `version.json` en la rama `main`. Si la versión es mayor que la instalada, descarga el instalador del último release y se actualiza sola. Para una versión nueva, cambia el número en estos 5 sitios y súbelo a `main`:
`build.ps1`, `App.cs` (3 líneas), `Setup.cs` (3 líneas), `Services.cs` y `version.json`.
El tag del release debe ser `v` + ese mismo número (por ejemplo `v1.0.1`).
