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
- Versión **1.0.0**; la primera release aún no está publicada (Actions bloqueado por facturación → publicar con `publicar-release.ps1` en Windows o arreglar la facturación).
- El README todavía muestra la insignia de versión 2.2.0 y debe corregirse a 1.0.0.

## Comandos útiles (en `.claude/commands/`)
- `/sincronizar` — trae y revisa lo último del repo.
- `/commit` — prepara un commit en español bien explicado.
- `/release` — guía para subir versión y publicar.
