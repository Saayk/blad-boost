---
description: Trae lo último del repo y revisa los commits nuevos de otras personas
---
1. Ejecuta `git fetch origin main` y muestra `git log --format='%h %an %ad%n  %s' HEAD..origin/main`.
2. Si hay commits nuevos, lee sus mensajes y sus diffs (`git show`) y resúmelos en español: quién, qué cambió y si afecta a lo que vamos a hacer.
3. Después haz `git pull --ff-only origin main`. Si no es fast-forward, NO fuerces: explica la divergencia y pregunta.
4. Muestra `git status -sb`.
