# Valkur — 2D Roguelike (Unity)

> **Single source of truth for Claude when working in this repo.** Read this before any other file.

## What this project is

**Valkur** is a 2D roguelike action game (real-time combat + spells with NPC casting, FSM monster AI with boss phases, tilemap world with Y-sort, procedural chunk-streamed dungeons, weighted loot, quest system, skill tree, day/night cycle, in-game tile/map/buildings/entities editors). It runs on **Unity 2022.3.62f1 LTS (URP 2D / C#)**.

Valkur was originally prototyped in Python (Pygame-CE). The Unity port is **complete**; the Python implementation has been archived and removed from `main` (see the `archive/python-legacy-*` git tag if you need to reference the original implementation). Pylos and Soluna minigames are **permanently deprecated** — never propose, plan, or list them.

Inspiration project (architecture only, do not copy code wholesale): `unity/Udemy_Inspiration/DungeonGunnerCourse/`.

## Workspace map

| Path | Purpose | Editable? |
|---|---|---|
| `unity/Valkur/Assets/_Project/` | Primary Unity code & assets | ✅ Yes |
| `unity/Valkur/Assets/Tests/` | EditMode + PlayMode test suites | ✅ Yes |
| `unity/Udemy_Inspiration/DungeonGunnerCourse/` | Architectural inspiration only | 🔒 Read-only |
| `tools/` | Standalone Python utilities (audio analysis, atlas audits, overlay generation) | ✅ Yes |
| `.github/skills/` | Detailed skill knowledge bases (shared with Copilot) | ✅ Yes |
| `.github/agents/` | Copilot agent specs (parallel to `.claude/agents/`) | ✅ Yes |
| `.claude/agents/` | Claude Code agent specs | ✅ Yes |
| `.claude/commands/` | Claude Code slash commands | ✅ Yes |

## Cardinal rules (must follow always)

1. **The Unity MCP console MUST be clean before declaring any task done.** After every C# change run `mcp_unity_refresh_unity` (compile=request, mode=force, scope=scripts, wait_for_ready=true) followed by `mcp_unity_read_console` (types=["error","warning"], format=detailed). Fix every error and every actionable warning. The terminal output of the test runner / Unity batch must also be clean. If the console can't be read because Unity isn't running, say so — don't pretend it's clean.
2. **Never modify `unity/Udemy_Inspiration/`** — reference only.
3. **Check existing scripts before creating new ones.** Many systems have multiple partial files; duplicates are the #1 source of regression.
4. **Edit ScriptableObjects, not external JSON.** Catalog data lives in `.asset` files and is edited via the Inspector (or via in-game runtime editors F1/F3/F4/F5/F6/F7/F8/F10/F11/F12/Ctrl+F3). World-state JSON under `StreamingAssets/` is written by the runtime editors via the `IRepository` pattern — don't hand-edit it.
5. **Never read `Mouse.current` / `Keyboard.current` / `UnityEngine.Input.*` directly outside the Input core helpers.** Use the centralized fachadas — see "Input pipeline" below.

## Input pipeline (single source of truth)

Every input read in Valkur goes through one of four centralized helpers. Touching `Mouse.current` / `Keyboard.current` / `UnityEngine.Input` directly anywhere else is a regression: it breaks under the recurring Unity 2022.3 Editor "InputSystem drops events" bug.

| Helper | Location | Use for |
|---|---|---|
| **`InputService`** | `Scripts/Core/Input/InputService.cs` | Bindings — exposes `UI.Click`, `Gameplay.Move`, `Editors.ToggleTile`, etc. from the canonical `ValkurInputActions.inputactions` asset. THIS is the binding source of truth. |
| **`MouseInputManager`** | `Scripts/Core/Input/MouseInputManager.cs` | Mouse buttons + position + wheel. `IsLeftMouseButtonPressed()`, `WasLeftMouseButtonReleasedThisFrame()`, `GetScreenMousePosition()`, `GetMouseWheelDelta()`, etc. ORs new InputSystem with legacy `UnityEngine.Input` automatically. |
| **`KeyboardInputManager`** | `Scripts/Core/Input/KeyboardInputManager.cs` | Keyboard keys. `WasKeyPressedThisFrame(Key, KeyCode)`, `IsCtrlHeld()`, `WasEnterPressedThisFrame()`, `WasEscapePressedThisFrame()`, etc. Same OR-fallback pattern. |
| **`InputCompat`** | `Scripts/Core/Input/InputCompat.cs` | Semantic menu helpers — `NavUpPressed()`, `ConfirmPressed()`, `CancelPressed()`. Wraps `KeyboardInputManager`. |
| **`EditorHotkeyBindings`** | `Scripts/Core/Input/EditorHotkeyBindings.cs` | F1–F12 hotkeys + Ctrl/Alt modifiers. Stateless API: `WasPerformedThisFrame(Hotkey.ToggleTile)`. Resolves the live action from `InputService.Editors` on every call (immune to zombie-after-hot-reload). |

The **only legitimate exceptions** to the rule are:

- The four core helpers themselves (they obviously read from both backends).
- Diagnostic / boot-race null-checks (`if (Mouse.current == null) ...`).
- `mouse.delta.ReadValue()` for raw mouse-delta which `MouseInputManager` doesn't expose yet — flag as a TODO if you find a third callsite. Scroll wheel IS centralized: use `MouseInputManager.GetMouseWheelDelta()`.

If you need a key the existing helpers don't expose (e.g. `KeyboardInputManager.WasF2PressedThisFrame()` for F2-rename), add the helper rather than a new direct read.

The legacy axes in `ProjectSettings/InputManager.asset` (`Horizontal`, `Vertical`, `Fire1`, `Mouse X`, etc.) are kept as Unity defaults but are **inert** — no gameplay code reads them via `GetAxis`/`GetButton`. The remaining `UnityEngine.Input.*` calls inside the helper files (and the three callers `PlayerController.Movement.cs` / `InventoryUI.cs` / `TileEditorInputHandler.cs`) are deliberate OR-gates: they re-read the legacy backend whenever the new InputSystem may have dropped events. Don't "clean these up" — they exist to survive the recurring Unity 2022.3 Editor InputSystem event-drop bug.

## Unity assemblies & dependency rule

| Assembly | Path | May reference |
|---|---|---|
| `Valkur.Core` | `Scripts/Core/` | — |
| `Valkur.Data` | `Scripts/Data/` | Core |
| `Valkur.Infrastructure` | `Scripts/Infrastructure/` (incl. `Persistence/Profile/`: `IProfileDb`, `JsonProfileDb`, `InMemoryProfileDb`) | Core, Data |
| `Valkur.Gameplay` | `Scripts/Gameplay/` | Core, Data, Infrastructure |
| `Valkur.UI` | `Scripts/UI/` | Core, Data, Infrastructure |
| `Valkur.Editor` | `Scripts/Editor/` | All above |

**Forbidden:** `Valkur.Gameplay → Valkur.UI` (circular). Cross-system signaling goes through `ServiceLocator` or `GameEvents`.

## `Scripts/Gameplay/` folder layout

The Gameplay assembly is subdivided by feature so any single folder stays under ~20 files. When extending, place files in the matching subfolder (or create one rather than dumping into a flat root).

| Folder | Contents |
|---|---|
| `Bootstrap/` | Game/EntitySetup, DevConsole, scene composition |
| `Chat/` | In-game chat |
| `Combat/Resources/` | Health, Mana, Experience |
| `Combat/Damage/` | FloatingDamageNumber/Spawner, GrayscaleDeath, DeathDropSystem |
| `Combat/Mechanics/` | MeleeCombat, DashAbility, MouseTargetDetector, ComboCounter, NPCRespawnSystem |
| `Combat/Feedback/` | CastOutline, CombatFeedback, CombatAudioSystem, ToastSystem, ExplosionEffect, CombatRangeVisualizer |
| `Combat/WorldUI/` | WorldHealthBar, WorldManaBar, WorldDashBar, FacingIndicator |
| `Combat/Lifecycle/` | TimedDespawn, SpawnStabilizer |
| `Combat/StatusEffects/` | Status effect implementations (Burn, Poison, Stun, Freeze, Slow) |
| `Editors/_Shared/` | EditorCameraPanController, EditorUIHelpers (cross-editor) |
| `Editors/Buildings/` | Buildings runtime editor (F10) — partials + UIBuilder + Outline + PerfProbe |
| `Editors/Entities/` | Entities runtime editor — partials + UIBuilder + Outline |
| `Editors/FSM/` | FSM runtime editor — partials + UIBuilder |
| `Editors/Inventory/` | Inventory runtime editor — partials + UIBuilder |
| `Editors/Items/` | Items runtime editor — partials + UIBuilder |
| `Editors/Lighting/` | Lighting runtime editor — partials |
| `Editors/Map/` | Map runtime editor (F11) |
| `Editors/Particles/` | Particles runtime editor — partials + UIBuilder |
| `Editors/Spells/` | Spells runtime editor — partials + UIBuilder + SpellPreviewGraphic |
| `Editors/Tile/` | Tile runtime editor (F6) |
| `Enemies/` | NPC AI, FSM behaviors, NPCAutoCast, NPCCastState, BossPhaseController, BossConfigurator |
| `HUD/` | In-world HUD overlays + modal panels: SpellBarHUD, BossHealthBarHUD, QuestLogHUD, SkillTreeHUD, StatisticsHUD |
| `Inventory/` | Inventory model + UI runtime |
| `Player/` | PlayerController, LearnedSkills, SkillEffectApplicator, AuraRegistry, HpRegenAura, LevelUpSkillPointSystem |
| `Quests/` | IObjective, KillCountObjective, Quest aggregator, QuestManager |
| `Save/` | Save/load systems, PermadeathSaveCleanupSystem, ProfileTelemetrySystem |
| `Spawners/` | Entity spawners |
| `Spells/Core/` | ISpellExecutor, SpellCaster, SpellCaster.Execution |
| `Spells/Executors/` | `*Executor.cs` (Projectile, Area, Slash, Dash, …) |
| `Spells/Controllers/` | Aura, Beam, Mine, Puddle, Shield, Summon, Totem, Vortex, Wall, MeteorStrike, Cone, ArcaneFlame |
| `Spells/Projectiles/` | Projectile, BoomerangProjectile, IProjectileVisual |
| `Spells/Visuals/` | ElementalProjectileVisual, FireballVisual, FireballImpactFX, LightningBoltFX, MeteorMissileFX, AreaFXRig |
| `UIKit/` | Reusable runtime UI primitives (own asmdef — leave alone) |
| `Vendors/` | Shop / vendor logic |
| `VFX/` | Pooled VFX |
| `World/Dungeon/` | DungeonGenerator, DungeonLoader.*, TilemapLayerSetup, debug overlays |
| `World/Buildings/` | BuildingLoader.*, BuildingObject.*, BuildingCollisionLoader.*, debug overlays |
| `World/Zones/` | ZoneManager.*, ZoneDatabaseLoader, ZonePortal |
| `World/Navigation/` | PathFinder, SpatialHash, NPCSeparationSystem, YSortEntity |
| `World/Lighting/` | WorldLightLoader, DayNightCycle |
| `World/Setup/` | WorldLoader, OverlayLoader, CameraSetup, WorldGridBuilder |
| `World/Pickups/` | CoinPickup |
| `World/_Util/` | MiniJsonRuntime |

Namespaces are independent of folder paths — `using Valkur.Gameplay.Buildings;` resolves regardless of whether the file lives in `Gameplay/World/Buildings/` or elsewhere. Use `git mv` (preserves `.meta` GUIDs) when relocating files.

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

## Pixel-art conventions

| Concept | Value | Notes |
|---|---|---|
| World PPU (most assets) | 16 | 1 world unit = 16 px |
| Buildings PPU | 32 | Buildings have a higher PPU for finer detail |
| Tiles PPU | 32 | Audited via `tools/atlas/audit_tile_sizes.py` |

## Asset organization & naming

The full convention lives in `.github/skills/asset-pipeline/SKILL.md` (sections **Naming Convention**, **Where assets live**, **Forbidden patterns**). The summary:

- Top-level `_Project/` folders are `PascalCase` (`Art`, `Audio`, `Data`, `Resources`, `SpriteAtlases`, …); everything inside is `snake_case` (`art/items/alchemy/`, `audio/sfx/inventory/`).
- File names: `snake_case`, lowercase extensions, English only, no spaces / `(` / `,` / `'`. Never `*_old.png`, `*_copy.png`, `ChatGPT *.png`.
- Vendor / asset-store packs go under `<Layer>/Vendor/<PackName>/` (e.g. `Art/VFX/Vendor/SlashVFX/`), never at `Assets/` root.
- `Resources/` is loaded whole at build — keep it minimal (only assets actually loaded by `Resources.Load<T>`).
- Sprite atlases live in **one** place: `_Project/SpriteAtlases/`.
- Backups don't go in `Assets/` — git is the backup. No `_backups/`, `Backups/`, `*_old.*` allowed.
- The lint script `tools/atlas/audit_asset_conventions.py` and the EditMode test `AssetConventionsTests` enforce the rules — run them before any large asset import.

## Where data lives

| Data | Source of truth |
|---|---|
| Audio (music + SFX + scopes + ducking) | `Resources/AudioCatalog.asset` (edit via Inspector or `Valkur > Audio > Music Scanner`) |
| Music BPM / beat metadata | Same asset (per-track fields), or `tools/audio/{analyze_music,patch_audio_catalog_bpm}.py` |
| Items | `Data/Catalogs/Items/ItemCatalog.asset` |
| Monsters | `Data/Catalogs/Monsters/*.asset` (catalog at `MonsterCatalog.asset`) |
| Spells | `Data/Catalogs/Spells/SpellCatalog.asset` (edit via Inspector or F4 in-game) |
| Buildings | `Data/Catalogs/Buildings/BuildingCatalog.asset` (edit via F10 in-game) |
| Particles | `Data/Catalogs/Particles/ParticlePresetCatalog.asset` (edit via F1) |
| Spawners | `Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset` (edit via F3) |
| Lighting Presets | `Data/LightPresetCatalog.asset` (edit via Ctrl+F3) |
| Chat Personas / Assignments | `Data/ChatPersonas/*.asset` + `ChatAssignmentCatalog.asset` |
| Vendors | `Data/Vendor/{EconomyGroups,Configs}/*.asset` |
| Players | `Data/Catalogs/Players/*.asset` |
| World state (placed buildings, lights, spawners, particles, tile overlays) | `StreamingAssets/{Buildings,Lights,Spawners,Particles,Maps}/*.json` (written by F1/F3/F8/F10/F11/Ctrl+F3) |
| FSM (states, assignments, animation map) | `StreamingAssets/FSM/*.json` (written by F12) |
| Player saves + run history | `Application.persistentDataPath/{Saves,profile.json}` (atomic-write + checksum + 5 rotating backups) |

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
# Unity tests via MCP (preferred)
#   mcp_unity_run_tests(mode="EditMode", include_failed_tests=true)
#   poll: mcp_unity_get_test_job(job_id=...)

# Unity tests via CLI (fallback)
"C:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe" \
  -batchmode -nographics -silent-crashes \
  -projectPath unity/Valkur \
  -runTests -testPlatform EditMode \
  -testResults TestResults.xml -logFile -
```

## Specialized agents (`.claude/agents/`)

Use the right agent for the right job. Each agent has a constrained scope and project-specific rules.

| Agent | When to use |
|---|---|
| `unity-architect` | New gameplay feature, system refactor, general C# work |
| `unity-mcp-guardian` | Verify console clean after a batch of edits; fix lingering errors/warnings |
| `unity-tester` | Create/fix/run tests; enforce namespaces; audit coverage |
| `asset-pipeline` | Sprite/audio/atlas migration; PPU/pivot policies |
| `buildings-editor` | Anything involving the Buildings Editor (window or runtime F10) |
| `tile-editor` | Anything involving the Tile Editor (F6) |
| `editor-ux-parity` | Audit / enforce UI/UX parity across in-game runtime editors |
| `editor-wiring-auditor` | Audit how a runtime editor is wired into bootstrap, services, hotkeys |
| `refactor-modularizer` | Split oversized files; extract reusable helpers; remove dead code |
| `udemy-inspiration` | Pull architectural patterns from DungeonGunnerCourse |

## Slash commands (`.claude/commands/`)

| Command | What it does |
|---|---|
| `/unity-clean` | Refresh Unity, read console, report (and fix if asked) |
| `/unity-tests` | Run EditMode (or both) test suite via MCP, poll, report |
| `/unity-test-new <System>` | Scaffold a test in correct folder/namespace |
| `/unity-status` | Console + last test summary at a glance |

## Skills (`.claude/skills/` and `.github/skills/`)

Skills are knowledge bases; agents and commands load them as needed. Authoritative content lives under `.github/skills/` (shared with Copilot); `.claude/skills/` are thin Claude wrappers that point to the same files.

| Skill | Source-of-truth file |
|---|---|
| unity-development | `.github/skills/unity-development/SKILL.md` |
| unity-testing | `.github/skills/unity-testing/SKILL.md` |
| asset-pipeline | `.github/skills/asset-pipeline/SKILL.md` |
| markdown-docs | `.github/skills/markdown-docs/SKILL.md` |
| valkur-conventions | `.github/skills/valkur-conventions/SKILL.md` |

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
- **F10 Buildings save position-collapse bug** — root cause unknown but mitigated by 3 guards in `BuildingsRuntimeEditor.Persistence.cs`. If the F10 save ever logs `ABORTING save — ...`, that's this bug firing. Read `.github/incidents/BUILDINGS_SAVE_POSITION_COLLAPSE.md` for the recovery procedure and the next-step investigation checklist.

## Incident reports

Past incidents that left investigation hooks behind. Read these first when a
related symptom reappears.

| Incident | When | Doc |
|---|---|---|
| F10 Buildings save collapses `rel_x`/`rel_y` to one position per zone | 2026-05-08 (mitigated, root cause TBD) | `.github/incidents/BUILDINGS_SAVE_POSITION_COLLAPSE.md` |
| Run "twin-save" — duplicate `Saves/<runId>/` folders with byte-identical body but distinct `meta.run_id` | 2026-05-08 (mitigated — root cause: EditMode test pollution; fixed by `RefuseWriteOutsidePlayMode` guard) | `.github/incidents/RUN_TWIN_SAVE.md` |

## Open work

- **Asset pipeline Phase 2** — atlas consolidation + finalised `asset_map.csv` schema. Bulk reimport already executed; `ValkurAssetPostprocessor` writes Uncompressed platform overrides; what remains is the formal naming convention + `SpriteAtlas` group build for the 9 planned domain atlases.
- **Boss music wiring** — `BossPhaseController.OnPhaseChanged` is not yet wired to `AudioManager.PlayMusic`. Audio side is ready (catalog supports per-scope track resolution); needs the wiring.

The **`Valkur.Infrastructure.Persistence.Profile`** layer (run history, kill stats, achievements, profile counters, statistics HUD) lives behind `IProfileDb` (`JsonProfileDb` today; SQLite ready as a drop-in once row counts justify it) — see `.github/SQLITE_MIGRATION_AUDIT.md`.
