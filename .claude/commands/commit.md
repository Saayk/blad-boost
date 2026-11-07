---
description: Crea un commit en español, profesional y bien explicado
---
1. Revisa `git status` y `git diff` para entender TODO lo que cambió; comprueba que no hay secretos ni binarios.
2. Ejecuta primero `/sincronizar` si no lo hiciste en esta sesión.
3. Agrupa por cambio lógico y crea el commit con este formato, en español:
   - Título: `tipo: resumen en imperativo` (tipos: feat, fix, docs, refactor, chore), máx. 72 caracteres.
   - Cuerpo: "Qué cambia" y "Por qué", más cómo se probó (o que no se pudo probar).
4. Haz `git push origin <rama>` (nunca force). Informa el hash del commit.
