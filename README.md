<div align="center">

# ⚡ Blad Boost

**Más FPS, menos tirones. Un optimizador de PC para jugadores, hecho por BLAD Studios.**

![Plataforma](https://img.shields.io/badge/plataforma-Windows%2010%2F11-0078D6?logo=windows&logoColor=white)
![Versión](https://img.shields.io/badge/versi%C3%B3n-1.0.0-brightgreen)
![Lenguaje](https://img.shields.io/badge/C%23-WPF-512BD4?logo=dotnet&logoColor=white)
![Licencia](https://img.shields.io/badge/licencia-MIT-blue)

[**⬇️ Descargar**](https://github.com/Saayk/blad-boost/releases/latest) · [Reportar un problema](https://github.com/Saayk/blad-boost/issues) · [Contribuir](#-contribuir)

</div>

---

## 🎮 ¿Qué es Blad Boost?

Blad Boost (GameBoost) es una aplicación de Windows que **prepara tu PC para jugar**. Analiza tu hardware, aplica los ajustes que realmente ayudan y te explica qué hace cada uno, sin tocar nada a ciegas. Todo en una sola ventana, en español.

Pensada para quien quiere mejor rendimiento sin pasar horas en foros y guías de optimización.

## ✨ Funciones

| | Función | Qué hace |
|---|---|---|
| 🏠 | **Panel** | Resumen de tu PC y de las optimizaciones recomendadas que aún no aplicaste. |
| 🕹️ | **Juegos** | Detecta juegos de Steam, Epic y Riot (o los agregas a mano) y los lanza optimizados. |
| 🚀 | **Modo Juego** | Un clic: pausa servicios de Windows durante la partida, cierra apps en segundo plano y libera RAM en espera. |
| 🛠️ | **Optimizar** | Ajustes del sistema según tu hardware (Intel híbrido, Ryzen X3D, portátil, gráficos integrados o dedicados…). |
| 🧹 | **Limpieza** | Borra temporales y volcados de errores, limpia la caché DNS y revisa el espacio de tu disco. |
| 📊 | **Benchmark** | Mide el rendimiento de tu PC (≈15 s) y guarda un historial para comparar antes y después. |
| 💡 | **Consejos** | Recomendaciones personalizadas: XMP/EXPO, drivers de GPU, ratón a 1000 Hz+, batería, y más. |
| 📦 | **Apps recomendadas** | MSI Afterburner, HWiNFO, CPU-Z, CrystalDiskInfo, OBS Studio y otras herramientas útiles, con enlaces oficiales. |
| 🔄 | **Autoactualización** | Comprueba al iniciar si hay una versión nueva y se actualiza. |

Además: inicia con Windows minimizado en la bandeja, detecta tus juegos al abrirlos y **crea un punto de restauración** antes de aplicar cambios para que puedas revertir.

## 📥 Instalación

1. Ve a la [última release](https://github.com/Saayk/blad-boost/releases/latest).
2. Descarga **`GameBoost_Setup.exe`** (instalador) o **`GameBoost.exe`** (portable).
3. Ejecútalo como administrador.

**Requisitos:** Windows 10/11 de 64 bits · .NET Framework 4.x (ya incluido en Windows) · permisos de administrador.

## 🔧 Compilar desde el código

En Windows, desde PowerShell:

```powershell
./build.ps1
```

Genera `GameBoost.exe` y `GameBoost_Setup.exe` en `dist/` y actualiza `version.json`.

## 🗂️ Estructura del proyecto

```
blad-boost/
├── App.cs · MainWindow.xaml     Interfaz principal y lógica de la app
├── Core.cs                      Motor de optimización y detección de hardware
├── Services.cs                  Instalador, desinstalación y actualizaciones
├── Lang.cs · check-lang.ps1     Traducciones y validador de cadenas
├── Setup.cs · Setup.xaml        Instalador gráfico
├── build.ps1                    Script de compilación
└── version.json                 Feed de actualizaciones
```

## 🤝 Contribuir

¡Las contribuciones son bienvenidas!

1. `git pull` antes de empezar para tener lo último.
2. Crea una rama: `git checkout -b mi-cambio`.
3. Haz commits claros que expliquen **qué** cambias y **por qué**.
4. Si tocas textos, ejecuta `check-lang.ps1`.
5. Abre un Pull Request describiendo el cambio.

## ⚠️ Aviso

Blad Boost modifica ajustes de Windows. Se crea un punto de restauración antes de aplicar cambios, pero úsalo bajo tu responsabilidad. El software se entrega "tal cual", sin garantías.

## 📄 Licencia

[MIT](LICENSE) © 2026 BLAD Studios
