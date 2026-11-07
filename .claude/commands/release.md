---
description: Guía para subir de versión y publicar una release
---
1. Sincroniza (`/sincronizar`) y confirma con la persona cuál es el número de versión nuevo.
2. Cambia la versión en los 5 sitios: `build.ps1`, `App.cs` (3 líneas), `Setup.cs` (3 líneas), `Services.cs`, `version.json`; actualiza las `notes` de `version.json` y `build.ps1`.
3. Commit en español (`/commit`) y push.
4. Publicar: si Actions funciona, el cambio en `version.json` dispara la release. Si no, en Windows ejecuta `powershell -ExecutionPolicy Bypass -File publicar-release.ps1` (necesita `gh auth login`). Si estás en Linux/nube, no puedes compilar: dilo y deja los pasos.
5. Verifica que la release `vX.Y.Z` existe con `GameBoost_Setup.exe`.
