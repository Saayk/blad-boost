# Prompt inicial para una sesión local de Claude Code

## Preparar (una sola vez, en el PC con Windows)
```powershell
git clone https://github.com/Saayk/blad-boost.git
cd blad-boost
claude
```
Requisitos: [Git](https://git-scm.com/download/win), [Claude Code](https://claude.com/claude-code) y, para releases, [GitHub CLI](https://cli.github.com/) con `gh auth login`. Cada persona se identifica con su cuenta (`git config user.name` / `user.email`).

## Prompt para pegar al abrir la sesión
```text
Estás en el repo de Blad Boost (https://github.com/Saayk/blad-boost), un optimizador de PC para juegos en Windows hecho en C# WPF (.NET Framework 4). Trabajo junto a mi compañero y los dos usamos Claude Code sobre este mismo repo.

Antes de hacer nada:
1. Lee CLAUDE.md, README.md y COMPILAR.md y sigue las reglas de CLAUDE.md.
2. Ejecuta /sincronizar: trae origin/main, lee los commits y diffs nuevos de la otra persona y resúmemelos en español.
3. Dime en qué estado está el proyecto (versión, si hay release publicada, qué falta) y confirma que estás conectado al repo (git remote -v, git status).

Reglas: commits siempre en español, profesionales y bien explicados (qué cambia y por qué); nunca force push; sin binarios ni secretos en git; si no puedes compilar o probar algo, dímelo en vez de asumir que funciona. Pregúntame antes de publicar releases o hacer cambios difíciles de revertir.

Cuando termines de orientarte, pregúntame qué quiero hacer hoy.
```

## Día a día
- Empezar: `/sincronizar`
- Terminar un cambio: `/commit`
- Sacar versión nueva: `/release`
