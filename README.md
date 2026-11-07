# Blad Boost

Optimizador de PC para juegos en Windows, de **BLAD Studios**. Aplica ajustes del sistema para mejorar el rendimiento, detecta tu hardware (CPU, GPU, RAM, disco, portátil/escritorio) y recomienda drivers y herramientas útiles.

## Descarga

Descarga el instalador desde la [última release](https://github.com/Saayk/blad-boost/releases/latest):

- **GameBoost_Setup.exe** – instalador (recomendado)
- **GameBoost.exe** – versión portable

> La app se actualiza sola consultando `version.json` de este repositorio.

## Requisitos

- Windows 10/11 de 64 bits
- .NET Framework 4.x (incluido en Windows)
- Permisos de administrador (necesarios para aplicar los ajustes)

## Compilar desde el código

En Windows, desde PowerShell:

```powershell
./build.ps1
```

El script compila `GameBoost.exe` y el instalador `GameBoost_Setup.exe` en `dist/` y genera `version.json`.

## Estructura del proyecto

| Archivo | Descripción |
|---|---|
| `App.cs`, `MainWindow.xaml` | Interfaz principal y lógica de la app |
| `Core.cs` | Motor de optimización y detección de hardware |
| `Services.cs` | Instalador, desinstalación y sistema de actualizaciones |
| `Lang.cs` | Traducciones (`check-lang.ps1` valida las cadenas) |
| `Setup.cs`, `Setup.xaml` | Instalador gráfico |
| `version.json` | Feed de actualizaciones |

## Contribuir

1. Haz `git pull` antes de empezar.
2. Crea una rama (`git checkout -b mi-cambio`) y haz commits claros que expliquen **qué** y **por qué**.
3. Ejecuta `check-lang.ps1` si tocas textos.
4. Abre un Pull Request.

## Licencia

[MIT](LICENSE) © 2026 BLAD Studios
