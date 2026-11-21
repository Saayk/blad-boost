# Blad Boost — contexto del proyecto para Claude

Este archivo lo lee Claude Code automáticamente al abrir una sesión en esta carpeta. Léelo entero antes de tocar nada.

## Qué es
**Blad Boost** (internamente *GameBoost*) es un optimizador de PC para juegos en **Windows**, de BLAD Studios. Aplica ajustes del sistema según el hardware, tiene Modo Juego, limpieza, benchmark, consejos y se autoactualiza. Interfaz en español.

- **Repo:** https://github.com/Saayk/blad-boost (público, licencia MIT)
- **Dueño:** Saayk · **Colaborador:** Leo Bogantes. Ambos usan Claude Code, así que **el historial lo escriben varias personas y sesiones**.
- **Rama principal:** `main`.

## Tecnología
- C# con WPF sobre **.NET Framework 4** (no .NET moderno). Se compila con el `csc.exe` que trae Windows, sin Visual Studio.
- **Solo compila en Windows.** Si estás en Linux/nube no puedes compilar ni probar la app: dilo claramente en vez de afirmar que funciona.

## Mapa del código
| Archivo | Para qué sirve |
|---|---|
| `App.cs`, `MainWindow.xaml` | Interfaz principal y lógica de la app |
| `Core.cs` | Motor de optimización y detección de hardware |
| `Crosshair.cs` | Crosshair superpuesto: ajustes (`crosshair.json`), dibujo, ventana transparente click-through y atajo global. La página está en `App.cs` (`BuildCrosshair`) |
| `AimTrainer.cs` | Entrenador de puntería (Flicks, Gridshot, Tracking, Reacción); marcas en `aim.json`. Página en `App.cs` (`BuildAim`) |
| `Macros.cs` | Macros: atajo global → pasos (texto, teclas, esperar, abrir) con `SendInput`; se pausan en Modo Juego o con un juego delante. Datos en `macros.json`. Página en `App.cs` (`BuildMacros`) |
| `Net.cs` | Medidas de red: ping ICMP, latencia TCP a regiones de AWS y comparativa de DNS. Página "Red y ping" en `App.cs` (`BuildNet`); los ajustes de red (Nagle, QoS, P2P...) están en `Core.cs` |
| `Services.cs` | Instalador, desinstalación y actualizaciones (`Feed` apunta a `version.json` en `main`) |
| `Lang.cs`, `check-lang.ps1` | Traducciones y validador de cadenas (textos con `Tr("...")` / `Tf(...)`) |
| `Setup.cs`, `Setup.xaml` | Instalador gráfico |
| `build.ps1` | Compila y genera `dist/` (exe, instalador, `version.json`) |
| `publicar-release.ps1`, `COMPILAR.md` | Publicar una release desde un PC con Windows |
| `.github/workflows/release.yml` | Release automática al cambiar `version.json` (hoy bloqueada por facturación de la cuenta) |

## Reglas de trabajo (obligatorias)
1. **Sincroniza antes de empezar:** `git fetch origin main`, mira `git log --oneline HEAD..origin/main` y **lee los commits y diffs nuevos** (los puede haber hecho la otra persona). Luego `git pull --ff-only origin main`. No sobrescribas ni reviertas trabajo ajeno sin avisar.
2. **Commits en español, profesionales y bien explicados:** título corto en imperativo (`feat:`, `fix:`, `docs:`, `refactor:`), y cuerpo con **qué cambia y por qué**. Un commit por cambio lógico.
3. **Nunca `force push`** ni reescribas el historial de `main`. Los cambios grandes van en una rama + Pull Request.
4. **Nada de binarios en git** (`.exe`, `dist/` están ignorados). Los ejecutables van como asset de una Release.
5. **No subas secretos** (tokens, contraseñas, rutas personales). Revisa el diff antes de cada commit.
6. **No inventes funciones ni resultados.** Si no pudiste compilar o probar, dilo.
7. Si tocas textos visibles, ejecuta `check-lang.ps1`.
8. Pregunta antes de acciones difíciles de revertir o visibles al público (releases, cambios de licencia, borrar ramas).

## Versiones y actualizaciones
La app lee `version.json` de `main`; si la versión es mayor que la instalada, descarga `releases/latest/download/GameBoost_Setup.exe`. Para una versión nueva cambia el número en **5 sitios** y súbelo:
`build.ps1`, `App.cs` (3 líneas), `Setup.cs` (3 líneas), `Services.cs` y `version.json`.
El tag de la release es `v` + ese número (ej. `v1.0.1`) y el instalador debe llamarse exactamente `GameBoost_Setup.exe`.

## Estado actual
- Versión **1.2.2** (pasos de ratón en las macros; la 1.2.1 añadió atajos con cualquier tecla o botón del ratón y tiempos, la 1.2.0 puntería y macros, la 1.1.0 el Crosshair), publicada como release `v1.2.2`. El instalador no está firmado: Smart App Control puede bloquear la autoactualización. El workflow de Actions sigue fallando (facturación): las releases se publican a mano con `publicar-release.ps1` en Windows, o con `csc.exe` + `gh release create` si no hay PowerShell.

## Comandos útiles (en `.claude/commands/`)
- `/sincronizar` — trae y revisa lo último del repo.
- `/commit` — prepara un commit en español bien explicado.
- `/release` — guía para subir versión y publicar.
