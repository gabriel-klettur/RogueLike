# Valkur — 2D Roguelike (Python → Unity Migration)

> **Single source of truth for Claude when working in this repo.** Read this before any other file.

## What this project is

**Valkur** is a 2D roguelike action game (real-time combat + spells, FSM monster AI, tilemap world with Y-sort, inventory, save/load, in-game tile and map editors). It is being **ported from Python (Pygame-CE) → Unity 2022.3.62f1 LTS (URP 2D / C#)**. Migration is ~90% complete.

Inspiration project (architecture only, do not copy code wholesale): `unity/Udemy_Inspiration/DungeonGunnerCourse/`.

## Workspace map

| Path | Purpose | Editable? |
|---|---|---|
| `unity/Valkur/Assets/_Project/` | Primary Unity code & assets | ✅ Yes |
| `unity/Valkur/Assets/Tests/` | EditMode + PlayMode test suites | ✅ Yes |
| `unity/Udemy_Inspiration/DungeonGunnerCourse/` | Architectural inspiration only | 🔒 Read-only |
| `python/src/roguelike_engine/` | Engine reference (map, tile, rendering, input, db) | 🔒 Read-only |
| `python/src/roguelike_game/` | Gameplay reference (ECS, systems, managers) | 🔒 Read-only |
| `python/src/roguelike_editors/` | In-game editors reference (tile, map, buildings) | 🔒 Read-only |
| `python/data/` | Source JSON catalogs (entities, spells, items, maps) | 🔒 Read-only |
| `python/tests/` | Pytest behavior reference | 🔒 Read-only |
| `.github/skills/` | Detailed skill knowledge bases (shared with Copilot) | ✅ Yes |
| `.github/agents/` | Copilot agent specs (parallel to `.claude/agents/`) | ✅ Yes |
| `.claude/agents/` | Claude Code agent specs | ✅ Yes |
| `.claude/commands/` | Claude Code slash commands | ✅ Yes |

## Cardinal rules (must follow always)

1. **The Unity MCP console MUST be clean before declaring any task done.** After every C# change run `mcp_unity_refresh_unity` (compile=request, mode=force, scope=scripts, wait_for_ready=true) followed by `mcp_unity_read_console` (types=["error","warning"], format=detailed). Fix every error and every actionable warning. The terminal output of the test runner / Unity batch must also be clean. If the console can't be read because Unity isn't running, say so — don't pretend it's clean.
2. **Never modify `python/src/`** — it is the migration source-of-truth. Read freely; do not edit.
3. **Never modify `unity/Udemy_Inspiration/`** — reference only.
4. **Check existing scripts before creating new ones.** Many systems are partially migrated; duplicates are the #1 source of regression.
5. **Preserve numerical parity with Python** — damage, speed, cooldowns, AoE radii, projectile speed, AI timings. Use the conversion table below.

## Unity assemblies & dependency rule

| Assembly | Path | May reference |
|---|---|---|
| `Valkur.Core` | `Scripts/Core/` | — |
| `Valkur.Data` | `Scripts/Data/` | Core |
| `Valkur.Infrastructure` | `Scripts/Infrastructure/` | Core, Data |
| `Valkur.Gameplay` | `Scripts/Gameplay/` | Core, Data, Infrastructure |
| `Valkur.UI` | `Scripts/UI/` | Core, Data, Infrastructure |
| `Valkur.Editor` | `Scripts/Editor/` | All above |

**Forbidden:** `Valkur.Gameplay → Valkur.UI` (circular). Cross-system signaling goes through `ServiceLocator` or `GameEvents`.

## Code style (C#)

- `[SerializeField] private` + `[Tooltip("…")]` for inspector fields. **Never** public fields.
- `ServiceLocator.Get<T>()` for cross-system access. **No raw singletons** (only `SingletonMonoBehaviour<T>` for true scene-wide managers).
- `ScriptableObject` for all designer-tunable data — no hardcoded tuning.
- Object pooling via `Scripts/Core/ObjectPool.cs` for projectiles, VFX, hit numbers.
- Static mutable state needs `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` reset (Domain Reload is OFF for fast iteration).
- One class per file; filename = class name.

## Layers

**Physics layers:** Player(8), NPC(9), Projectile(10), World(11), Pickup(12), UIBlocker(13), Building(14), Spawner(15).

**Sorting layers (depth):** Background → Ground → FloorDecals → ObjectsLow → WallsBottom → Entities → Decorations → WallsTop → ObjectsHigh → Projectiles → VFX → Overhead → UI_World → Overlay.

## Python → Unity unit conversions

| Python | Unity | Formula |
|---|---|---|
| px | world units | `÷ 16` (PPU=16; Buildings uses PPU=32) |
| px/tick (60Hz) | world units/s | `× 3.75` |
| px/tick² | world units/s² | `× 225` |
| ticks | seconds | `÷ 60` |

## Unity MCP setup (Claude Code)

The Unity ↔ Claude Code bridge runs in HTTP transport. Config lives in `.mcp.json` at repo root and points at `http://127.0.0.1:8080/mcp`. To bring it up:

1. **Unity side** — open `Window → MCP For Unity → Toggle MCP Window`. In the **Connect** tab make sure: Transport = `HTTPLocal`, port 8080, **Local Server = Started** (green dot, "Session Active (Valkur)"). In **Client Configuration** select `Claude Code` and click **Configure** once.
2. **Claude Code side** — `.mcp.json` registers the server automatically; restart the Claude Code session (close and reopen the chat) so the MCP client picks it up. The Unity tools (`refresh_unity`, `read_console`, `manage_editor`, `run_tests`, etc.) appear without further work.
3. **Common parameter pitfalls** (the ones that throw `ValidationError` in the FastMCP log):
   - `refresh_unity(mode=...)` accepts only `'if_dirty'` or `'force'` (not `'normal'`).
   - `manage_editor(action=...)` accepts `'play' | 'pause' | 'stop' | 'set_active_tool' | 'add_tag' | 'remove_tag' | 'add_layer' | 'remove_layer' | 'deploy_package' | 'restore_package' | 'undo' | 'redo' | 'telemetry_status' | 'telemetry_ping'` (not `'stop_play_mode'`).
   - `read_console(format=...)` accepts `'plain' | 'detailed' | 'json'` (not `'summary'`); there is no `max_entries` parameter.
   - `execute_menu_item` takes the menu path positionally — not as `path=` or `menu_item_path=`.

## Build & test

```bash
# Python tests (behavior reference)
cd python && python -m pytest tests/ -v

# Unity tests via MCP (preferred)
#   mcp_unity_run_tests(mode="EditMode", include_failed_tests=true)
#   poll: mcp_unity_get_test_job(job_id=...)

# Unity tests via CLI (fallback)
"C:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe" \
  -batchmode -nographics -silent-crashes \
  -projectPath unity/Valkur \
  -runTests -testPlatform EditMode \
  -testResults TestResults.xml -logFile -

# Data dry-run (in Editor)
#   Menu: Valkur > Migration > Dry-Run All (Validate Only)
```

## Specialized agents (`.claude/agents/`)

Use the right agent for the right job. Each agent has a constrained scope and project-specific rules.

| Agent | When to use |
|---|---|
| `unity-architect` | New gameplay feature, system refactor, general C# work |
| `unity-mcp-guardian` | Verify console clean after a batch of edits; fix lingering errors/warnings |
| `unity-tester` | Create/fix/run tests; enforce namespaces; audit coverage |
| `python-analyst` | Read & analyze Python source before porting |
| `data-migrator` | JSON → ScriptableObject conversion; dry-run validation |
| `asset-pipeline` | Sprite/audio/atlas migration; PPU/pivot policies |
| `buildings-editor` | Anything involving the Buildings Editor (window or runtime F10) |
| `tile-editor` | Anything involving the Tile Editor (F6) |
| `migration-qa` | Parity checks Python vs Unity; regression testing |
| `udemy-inspiration` | Pull architectural patterns from DungeonGunnerCourse |

Recommended workflow for porting a system:
```
python-analyst → data-migrator (if data) → unity-architect → unity-tester → unity-mcp-guardian
```

## Slash commands (`.claude/commands/`)

| Command | What it does |
|---|---|
| `/unity-clean` | Refresh Unity, read console, report (and fix if asked) |
| `/unity-tests` | Run EditMode (or both) test suite via MCP, poll, report |
| `/unity-port <python-path>` | Walk the porting workflow for a Python file |
| `/unity-test-new <System>` | Scaffold a test in correct folder/namespace |
| `/unity-status` | Migration progress + console + last test summary |

## Skills (`.claude/skills/` and `.github/skills/`)

Skills are knowledge bases; agents and commands load them as needed. Authoritative content lives under `.github/skills/` (shared with Copilot); `.claude/skills/` are thin Claude wrappers that point to the same files.

| Skill | Source-of-truth file |
|---|---|
| unity-development | `.github/skills/unity-development/SKILL.md` |
| unity-testing | `.github/skills/unity-testing/SKILL.md` |
| python-to-csharp | `.github/skills/python-to-csharp/SKILL.md` |
| asset-pipeline | `.github/skills/asset-pipeline/SKILL.md` |
| combat-migration | `.github/skills/combat-migration/SKILL.md` |
| migration-testing | `.github/skills/migration-testing/SKILL.md` |
| markdown-docs | `.github/skills/markdown-docs/SKILL.md` |

## Key gotchas (the pit traps)

- **Image + TMP on same GameObject** → `NullReferenceException`. Use parent (Image+Button) + child (TMP).
- **`InventorySlot` is a struct** — no `== null`. Use `.IsEmpty`.
- **EditMode tests + `renderer.material`** → leak warnings. Use `renderer.sharedMaterial` or `LogAssert.ignoreFailingMessages = true`.
- **`SpellDefinition`**: `cooldownDuration` (not `cooldown`); **`Health`**: `CurrentHp` (not `Current`).
- **`DashAbility`** lives in `Valkur.Gameplay.Combat` (not `Player`).
- **Zone names** use `OrdinalIgnoreCase` — pass consistent casing anyway.
- **Cinemachine** overrides `Camera.main.transform` every LateUpdate. Use `CameraSetup.DetachFollow()` to pan freely.
- **Custom GL drawing in URP** — use `RenderPipelineManager.endCameraRendering`, not `OnRenderObject` (`Camera.current` is null in URP).
- **Static mutable fields without reset** → MissingReferenceException after second Play (Domain Reload is OFF).
- **Sprite-Lit-Default with no Light2D** → black tiles. Use `Sprite-Unlit-Default` fallback (already wired in `WorldGridBuilder.ApplyUnlitFallbackIfNeeded()`).

## Open work (high-level)

- Asset pipeline Phase 2 (atlas consolidation, full `asset_map.csv`).
- Day/night lighting cycle.
- NPC spell casting (cast state is partial).
- Pylos minigame deferred; Soluna empty.
- Legacy InputManager axes still wired alongside New Input System.

For full status, system-by-system parity tables, and the 50-step roadmap, read `.github/MIGRATION_GUIDE.md`.
