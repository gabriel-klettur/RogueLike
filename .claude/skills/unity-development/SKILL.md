---
name: unity-development
description: General Unity development reference for Valkur — MCP workflow, console verification, project conventions (assemblies, layers, sorting), domain-reload safety, ServiceLocator, ScriptableObjects, ObjectPool, Cinemachine, URP 2D, Tilemap, UI, performance, gotchas. Load whenever writing or refactoring C# under `unity/Valkur/Assets/_Project/Scripts/`.
---

# Unity Development for Valkur

The full canonical knowledge base lives at:

**[.github/skills/unity-development/SKILL.md](../../../.github/skills/unity-development/SKILL.md)** (~540 lines, 20 sections)

Read it directly with the `Read` tool when you need any of:

| Need | Section |
|---|---|
| MCP for Unity workflow (resources vs tools, post-edit verification, payload sizing, multi-instance) | §1 |
| Project conventions (assembly placement, code style) | §2 |
| Domain-reload safety pattern (mandatory for static state) | §3 |
| ServiceLocator / SingletonMonoBehaviour / ScriptableObject / ObjectPool | §4 |
| Physics & layers (the 8 layers + 2D physics tips) | §5 |
| Sorting layers (depth order, the 14-layer chain) | §6 |
| Cinemachine & camera (DetachFollow pattern) | §7 |
| Input (legacy + New Input System; `GameEditorManager.AnyEditorActive`) | §8 |
| URP 2D specifics (`endCameraRendering`, Light2D, Sprite-Lit-Default trap) | §9 |
| UI conventions (uGUI, mouse+keyboard parity, modal patterns) | §10 |
| Tilemap (Grid, TilemapCollider2D + CompositeCollider2D) | §11 |
| Performance cheatsheet (anti-patterns → fixes) | §12 |
| Testing (EditMode / PlayMode / CLI / parity) | §13 |
| Editor extensions (Custom Inspector, MenuItem, PropertyDrawer, Gizmos) | §14 |
| Common gotchas table (the pit traps) | §15 |
| Hot reload workflow | §16 |
| Saving & persistence (`ISaveService`, `SaveSchemaMigrator`) | §17 |
| Audio (`IAudioService`, `AudioCatalog`, mixer routing) | §18 |
| Migration-specific rules (unit conversions table) | §19 |
| Quick MCP recipes (component add, hierarchy read, run menu item, asset search) | §20 |

## Closing reminders (always)

- **Verify the console after every change.** No exceptions.
- **Check existing code before creating new code.** Duplication is the #1 source of regression.
- **Game tuning lives in data, not code.** ScriptableObjects > hardcoded constants.
- **Static state needs a reset method.** Domain reload is OFF.
- **Preserve Python game-feel exactly.** Apply unit conversions; don't eyeball.
