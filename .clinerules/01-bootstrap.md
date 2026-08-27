# Bootstrap — mandatory context for any non-trivial task

Before writing code in this workspace, load context in this order:

1. **`CLAUDE.md`** at the repo root — cardinal rules, conventions, and the
   accumulated pit traps. It is the single most important file in the repo.
2. **`.clinerules/skills/valkur-conventions.md`** — the fast-lookup card
   (assemblies, layers, sorting, code style, pit-trap table).
3. **The relevant skill card** in `.clinerules/skills/` — each one points to
   the canonical long form under `.github/skills/<name>/SKILL.md`. Read the
   long form when the card says the topic matters to your task:

   | Task touches | Skill |
   |---|---|
   | C# under `unity/Valkur/Assets/_Project/Scripts/` | `unity-development` |
   | Tests under `unity/Valkur/Assets/Tests/` | `unity-testing` |
   | FPS / frame-time / GC / memory | `unity-performance` |
   | Importing or validating sprites/audio/atlases | `asset-pipeline` |
   | Particle presets / `ParticleEmitter` / F1 editor | `vfx-authoring` |
   | Any `.md` file (docs, roadmaps, audits) | `markdown-docs` |

4. **Search before you create.** Many systems are already partially migrated;
   duplicates are the #1 cause of regression. Use `search_codebase` on the
   relevant `Assets/_Project/Scripts/` subtree before adding anything.

## Boundaries that are never negotiable

- **Never modify** `unity/Udemy_Inspiration/` (read-only architectural reference).
- **Never modify** `Art/VFX/Vendor/` (read-only vendor pack).
- **Never hand-edit** `StreamingAssets/**/*.json` — world state goes through
  the `IRepository` pattern (`JsonFile*Repository`).
- **Never edit Python source** from the Unity side; the Python game at
  `python/` is the behavioral reference, not a dependency.
