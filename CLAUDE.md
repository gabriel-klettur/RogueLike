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
| `Editors/_Shared/Workspace/` | `EditorWorkspaceService` — the single owner of editor layout/session/selection persistence |
| `Editors/Camera/` | Camera Editor — no hotkey, opened from the General Editor; partials + UIBuilder + UIHoverHelp |
| `Editors/Buildings/` | Buildings runtime editor (F10) — partials + UIBuilder + Outline + PerfProbe |
| `Editors/Entities/` | Entities runtime editor — partials + UIBuilder + Outline |
| `Editors/FSM/` | FSM runtime editor — partials + UIBuilder |
| `Editors/Inventory/` | Inventory runtime editor — partials + UIBuilder |
| `Editors/Items/` | Items runtime editor — partials + UIBuilder |
| `Editors/Lighting/` | Lighting runtime editor — partials |
| `Editors/Map/` | Map runtime editor (F11) |
| `Editors/Particles/` | Particles runtime editor — partials + UIBuilder |
| `Editors/Spells/` | Spells runtime editor — partials + UIBuilder + SpellPreviewGraphic |
| `Editors/Tile/` | Tile runtime editor (F8) |
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
| `Spells/Visuals/` | ElementalProjectileVisual, FlameConeFX, LightningBoltFX, MeteorMissileFX, AreaFXRig, IceWallVisual, VortexFunnelFX, ShieldSphereFX, KiAuraFX |
| `UIKit/` | Reusable runtime UI primitives (own asmdef — leave alone) |
| `Vendors/` | Shop / vendor logic |
| `VFX/` | Pooled VFX |
| `World/Dungeon/` | DungeonGenerator, DungeonLoader.*, TilemapLayerSetup, debug overlays |
| `World/Buildings/` | BuildingLoader.*, BuildingObject.*, BuildingCollisionLoader.*, debug overlays |
| `World/Zones/` | ZoneManager.*, ZoneDatabaseLoader, ZonePortal |
| `World/Navigation/` | PathFinder, SpatialHash, NPCSeparationSystem, YSortEntity |
| `World/Camera/` | CameraFeelDirector (+ partials), CameraFeel facade, CameraFeelMath, CameraFeelState |
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
| Spells | `Data/Catalogs/SpellCatalog.asset` — note it sits beside `Catalogs/`, not inside `Catalogs/Spells/`, which holds the individual `*.asset` definitions (edit via Inspector or F4 in-game) |
| Buildings | `Data/Catalogs/Buildings/BuildingCatalog.asset` (edit via F10 in-game) — 1176 templates over 1174 sprites (two sprites carry a second template that differs in a field no instance can override: `curse_house_topdown` with/without its doorway, `totem_forest` solid/non-solid); every prop imported through the sheet pipeline is described by a `tools/atlas/generated/building_props_manifest*.json`, one per wave |
| Particles | `Data/Catalogs/Particles/ParticlePresetCatalog.asset` (edit via F1) |
| Spawners | `Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset` (edit via F3) |
| Camera feel (shake, kick, lead, smooth follow) | `Resources/CameraFeelProfile.asset` |
| Lighting Presets | `Data/LightPresetCatalog.asset` (edit via Ctrl+F3) |
| Chat Personas / Assignments | `Data/ChatPersonas/*.asset` (runtime half) + `Data/ChatPersonas/Profiles/*_profile.asset` (narrative half) + **`Resources/Chat/ChatAssignmentCatalog.asset`** — under `Resources/` because `ChatSystem` is `AddComponent`-ed onto a bare GameObject and has no inspector slot to be wired from. All of it is generated by `Valkur > Chat > Import Personas` from `tools/chat/generated/chat_personas_manifest.json`; the join to entities is `Valkur > Chat > Wire Entities To Personas` |
| Vendors | `Data/Vendor/Configs/*.asset` (5, generated by `Wire Entities To Personas`, seeded from `ItemDefinition.itemType`). `Data/Vendor/EconomyGroups/` does not exist yet — `VendorEconomyService` is null-safe without one |
| Players | `Data/Catalogs/Players/*.asset` |
| Player stats, talents, grimoire, curves | **`Resources/Progression/ProgressionCatalog.asset`** — under `Resources/` because `PlayerProgression` is `AddComponent`-ed onto the player and has no inspector slot to be wired from. It points at `Data/Progression/{XpCurve,LevelStatCurve}.asset`, five `SkillTrees/<class>/` and nine `SpellTrees/<school>/`, all generated by `Valkur > Progression > Seed Progression Content` |
| World state (placed buildings, lights, spawners, particles, tile overlays) | `StreamingAssets/{Buildings,Lights,Spawners,Particles,Maps}/*.json` (written by F1/F3/F8/F10/F11/Ctrl+F3). `Particles/particles_instances.json` is schema v4: each record carries its own `config` (the copy of the preset it was placed with, defaults omitted), and may carry the legacy `spawn_scale_x` / `spawn_scale_y` / `reach` size ratios from v3 |
| FSM (states, assignments, animation map) | `StreamingAssets/FSM/*.json` (written by F12) — four sets: `Monster_Default` (melee), `Monster_Caster`, `Monster_Boss` (no `FleeState`), `NPC_Passive` (Idle/Unconscious/Death only). All 19 monsters are assigned in `assignments.json`; the resolution order is `by_eid` → `by_archetype` → `MonsterDefinition.fsmSet` → hard-coded IdleState |
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
| `tile-editor` | Anything involving the Tile Editor (F8) |
| `particles-editor` | Particle presets, `ParticleEmitter`, VFX beauty work, Particles Editor (F1) |
| `spell-vfx-director` | Spell look & game-feel — slash/projectile/area silhouettes, timing, impact, hit-stop, camera shake |
| `editor-ux-parity` | Audit / enforce UI/UX parity across in-game runtime editors — chrome, gestures, workspace persistence, theme, feedback |
| `editor-workspace-architect` | The editor workspace persistence LAYER itself (`_Shared/Workspace/`, `DraggablePanel` state, the `GameEditorManager` hook, the store, the contract test). Never edits the sixteen editors |
| `editor-wiring-auditor` | Audit how a runtime editor is wired into bootstrap, services, hotkeys |
| `refactor-modularizer` | Split oversized files; extract reusable helpers; remove dead code |
| `performance-optimizer` | Data-driven FPS / frame-time / GC optimization via Profiler + Recorder API |
| `udemy-inspiration` | Pull architectural patterns from DungeonGunnerCourse |

## Slash commands (`.claude/commands/`)

| Command | What it does |
|---|---|
| `/unity-clean` | Refresh Unity, read console, report (and fix if asked) |
| `/unity-tests` | Run EditMode (or both) test suite via MCP, poll, report |
| `/unity-test-new <System>` | Scaffold a test in correct folder/namespace |
| `/unity-status` | Console + last test summary at a glance |
| `/unity-profile` | Capture Profiler/Recorder snapshot — CPU/GPU axis breakdown + GC baseline |

## Live reload (no Stop/Play)

Most loaders can re-read their authored data without leaving Play Mode. The commands live
in `Scripts/Gameplay/Bootstrap/DevConsole.Commands.Reload.cs` under the `reload` category:

```
reloadworld  (rw)   buildings, spawners, lights, particles, item drops for the active slot
reloadfsm           invalidate the FSM cache and rebuild every live monster brain
reloadtiles  (rt)   repaint the tilemap from JSON and re-bake colliders
map [slot]          list map slots, or hot-load one
reconfig            re-apply MonsterDefinition changes to living NPCs, keeping positions
respawnnpcs         kill everything and re-fire the spawners
```

`DevConsole.Execute(string)` is public, so all of it is reachable from PlayMode tests and
from `mcp__unity__execute_code` — an agent can trigger its own verification without anyone
touching the Game view.

Two per-machine EditorPrefs matter as much as any of this and are NOT in the repo:
`Script Changes While Playing` should be *Recompile After Finished Playing* and
`Auto Refresh` should be *Enabled Outside Playmode*. With the defaults, editing a script
mid-play reloads the domain WITHOUT re-running the `SubsystemRegistration` resets, leaving
a corrupted session you have to Stop out of anyway.

## Console verbosity

High-volume development logs are gated by `Scripts/Core/VerboseLog.cs` — off by
default, never deleted. Toggle from the in-game DevConsole (choice persists via
`PlayerPrefs`, survives Play-mode restarts):

```
verbose                  # list categories + state
verbose world on         # per-overlay / per-tilemap world loading detail
verbose settings on      # every GameSettings.Save
verbose collision on     # per-layer collision bake detail
verbose all off
```

Summary lines, warnings and errors are deliberately **not** gated. When adding a
log that fires per file / per tile / per frame, gate it with a category and use
the `Func<string>` overload so the string is never built while it's off.

## Skills (`.claude/skills/` and `.github/skills/`)

Skills are knowledge bases; agents and commands load them as needed. Authoritative content lives under `.github/skills/` (shared with Copilot); `.claude/skills/` are thin Claude wrappers that point to the same files.

| Skill | Source-of-truth file |
|---|---|
| unity-development | `.github/skills/unity-development/SKILL.md` |
| unity-performance | `.github/skills/unity-performance/SKILL.md` |
| unity-testing | `.github/skills/unity-testing/SKILL.md` |
| asset-pipeline | `.github/skills/asset-pipeline/SKILL.md` |
| vfx-authoring | `.github/skills/vfx-authoring/SKILL.md` |
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
- **3D VFX packs do not survive the URP 2D Renderer.** `Art/VFX/Vendor/SlashVFX/` is authored
  for a perspective camera: mesh particles (`m_RenderMode: 4` + FBX), sub-objects rotated onto
  the XZ plane, 3D `Light` components the 2D Renderer ignores, and a distortion grab-pass with
  no opaque source. Dropped into Valkur it renders as a flat sliver with no light. Every slash
  is now code-native (`SlashAttack` + `SlashProfile`); the pack is kept but unreferenced.
- **A slash's silhouette comes from its arc.** `SlashProfile` maps `arcRangeDegrees` to one of
  four families (Thrust ≤55°, Crescent ≤108°, Cleave ≤175°, Whirl above), which then fixes the
  beat lengths, trail, segment budget, shake and hit-stop. Damage sweeps with the drawn edge and
  reaches exactly `hitRadius` — the legacy path damaged in a circle 1.5× longer than its visual.
  `slash_regular` keeps its own authored implementation (`RegularSlashAttack`) on purpose.
- **`SortingConfig.Z_SKY` is a Z depth, not a sorting order.** `LightningBoltFX` passed it as
  `sortingOrder` on the **Entities** layer, so every bolt drew under wall tops, decorations and
  all other VFX. World-space effects belong on `LAYER_VFX` with a small order. The same file
  also assigned `lr.material` (cloning the shared material once per bolt) — use `sharedMaterial`.
- **A spell that can silently do nothing cannot be learned.** `lightning` shared the chain
  implementation, whose first act is `if (sorted.Count == 0) return;` — cast with no enemy in
  range it spent mana and drew nothing, which read as "the spell is invisible". Every executor
  must produce a visual on every successful cast, targets or not.
- **The camera is moved by moving its follow target, never by writing the camera.** All three
  `CinemachineTransposer` dampings are forced to 0 in `CameraSetup.Awake`, which makes the
  transposer an exact 1:1 copy of `Follow` — verified live as `camera == follow + (0,0,-10)`
  to within a fifth of a screen pixel. `CameraFeelDirector` owns a `[Camera Target]` proxy and
  writes only that. Writing `Camera.main.transform` instead means racing the brain, which is
  what the old `CameraShake` did and lost.
- **An integer pixel rect is not the same thing as an exact aspect ratio.** `SnapOrthoSize`
  guarantees whole screen pixels per art texel on the VERTICAL axis only — it solves
  `ortho = pixelHeight / (2 x snapPPU x N)`. The horizontal axis inherits that guarantee
  purely through `Camera.aspect`, so the viewport must be EXACTLY 2:1 in whole pixels.
  `AspectRatioEnforcer` used to round each axis independently: a 1366x768 window produced a
  1366x682 viewport, aspect 2.002933 — integer pixels, wrong ratio — and tile quad edges
  drifted mid-pixel across the screen, showing the black background as VERTICAL seam lines.
  It now quantises to `k*p` by `k*q` from the ratio reduced to integers, so one scalar drives
  both axes. Options > Video (`DisplaySettings`) only offers exactly-2:1 sizes for the same
  reason. Diagnose with `Valkur > Display > Report Viewport Alignment`, and remember the two
  are independent failures: a clean camera render plus visible lines means the Game View
  composite or the screenshot, not the game.
- **Never write `orthographicSize` for an effect.** `CameraPixelSnap` derives its lattice from
  the live ortho size, and `CameraSetup.SnapOrthoSize` keeps it on a ladder where one art texel
  is an integer number of screen pixels (3.000 px at ortho 5 on a 960 px viewport). A zoom
  punch of a few percent lands between rungs and makes every tile on screen crawl. There is no
  seam-legal zoom punch in a 16-PPU game — express weight through kick, shake frequency, trauma
  decay, hit-stop and lead freeze instead.
- **Reparenting during activation is silently refused.** `SetParent` inside `OnEnable`/
  `OnDisable` logs `Cannot set the parent of the GameObject X while activating or
  deactivating the parent Y` and does nothing — `ParticleProjectileVisual` attached its
  four trail emitters there, so every pooled projectile left them stranded at the pool
  origin, four console errors per cast. Defer the attach to the next `LateUpdate`; detach
  on the impact callback, which runs before the pool deactivates the object.
- **`SpriteRenderer.color` on an entity body has exactly one owner: `SpriteTintStack`.**
  Nine systems used to cache it as "the original", tint, and write the cache back — correct
  alone, wrong together. A monster hit while burning had the flash capture orange as its
  baseline and restore orange after the burn ended, permanently. Burn/Poison/Freeze/Slow/
  Stun, the hit-flash fallback, `GrayscaleDeath` and `TransporterFX` now each own a
  `TintLayer` and never touch the renderer; layers multiply so overlapping effects blend.
  The stack lives on the ENTITY ROOT — attaching one to a child renderer creates a second
  base colour and reopens the bug. `PlayerSpiritVisuals` is deliberately NOT migrated: it
  tints every child renderer, not just the body.
- **An auto-tile slot key has a polarity, and it is not guessable.** The Corner16 model keys
  slots by the SECONDARY terrain — `TerrainTileResolver.ResolveVariantForCell` calls
  `CornerMask(grid, cell, ruleset.TerrainSecondary)` — while the pixel analysis that generates
  the mapping (`tools/atlas/analyze_tile_edges.py`) orders materials by how much of the sheet
  they cover. Those two orders agree only by luck: measured across the five generated packs,
  four had the primary terrain as material 0 and `grass_dirt` had it as material 1, so no fixed
  rule works and the mapping is declared per pack in `PACK_PRIMARY_MATERIAL`. Both halves are
  internally consistent while disagreeing, so nothing fails loudly — a fully-grass field just
  resolves to the all-sand tile. It surfaces only end-to-end: resolve a synthetic island and
  assert the chosen sprite belongs to the slot the mask dictates.
- **`FindBaseRuleset` is not "the ruleset to paint with".** It excludes anything with a
  secondary terrain, which was right for the cardinal model (a transition sheet drew the A-to-B
  border, a separate base sheet drew solid A). A Corner16 sheet ALWAYS declares a secondary —
  its corners are what separate A from B — so that filter made every generated pack unreachable
  and the auto-brush reported "no ruleset" for the five packs built for it. Paint paths call
  `FindPaintRuleset`, which prefers a base ruleset and falls back to a Corner16 one.
- **Sprite-Lit-Default with no Light2D** → black tiles. Use `Sprite-Unlit-Default` fallback (already wired in `WorldGridBuilder.ApplyUnlitFallbackIfNeeded()`).
- **Two SpriteAtlas assets over the same folder** → Unity logs `Sprite X matches more than one built-in atlases` once *per sprite* (3077 warnings once) and ships the atlas twice. `SpriteAtlasBuilder` now refuses to build a group whose source folder is already packed by another atlas anywhere in the project.
- **Deleting a MonoBehaviour leaves prefabs with null component slots** — `m_Script: {fileID: 0}`, no guid, one console entry per slot on every import (2345 of them from the DungeonGunner removal). Strip with `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` via `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`; check for unresolved guids first, since those *are* recoverable information.
- **`Resources.LoadAll<T>("")` is a full-tree scan, not a filter.** The empty path
  deserializes every one of the ~7,400 assets under `Resources/` and only then keeps the
  ones matching `T` — so an asset whose `m_Script` no longer resolves logs "The referenced
  script (Unknown) on this Behaviour is missing!" on EVERY call. `SpawnPlayer` did this
  looking for `PlayerDefinition` (of which `Resources/` holds none), and paid for it with
  34 console errors per Play from the raw Udemy `Room_*_Catacombs_*.asset` files. Always
  pass a subfolder. Corollary: raw third-party ScriptableObjects whose script Valkur never
  imported must NOT live under `Resources/` — the Catacombs sources now sit in
  `Data/Dungeon/CatacombsSource/` and are read as YAML text by `CatacombsImporter`.
- **Do not create the EventSystem at `BeforeSceneLoad`.** The first scene's objects have not
  awoken yet, so `PersistentEventSystem` minted a second one and uGUI logged "There can be
  only one active Event System." the instant MainMenu's own `OnEnable` registered — once per
  boot. `Ensure(createIfMissing: false)` at boot, then adopt the scene's in the `sceneLoaded`
  pass. Related: `Object.Destroy` is deferred to end-of-frame, so a duplicate is still
  registered when you re-enable yours on the next line — set `enabled = false` first, which
  runs `OnDisable` synchronously. Any sync `SceneManager.LoadScene` needs
  `PersistentEventSystem.Pause()` before it, the way `LoadingScreenController` already does.
- **A warning that fires on every boot for a deliberate steady state is a bug in the warning.**
  Four separate ones trained the reader to scroll past the console: `TileCollisionDiagnostics`
  called the visual `Collision` tilemap "not baked" (its `TilemapCollider2D` is disabled ON
  PURPOSE — `WorldCollisionBaker` owns those cells via the `CollisionPhysics_*` sub-tilemaps);
  the Map Editor warned once per persisted zone whose offset is shelved, a state it
  deliberately preserves forever; `CameraFeelDirector` reported its own first proxy install as
  "something reassigned the follow target"; `SaveService` warned on a bootstrap race it
  retries out of by design. Gate the expected case (`VerboseLog`, or `Debug.Log`) and keep the
  warning for the case that will not heal — e.g. the save ordinal still missing 15 s in.
- **`main.duration` cannot be written while a ParticleSystem is playing.** `AddComponent<ParticleSystem>()`
  starts it immediately (`playOnAwake` defaults true), so configuring it inline fires
  "Setting the duration while system is still playing is not supported" and silently keeps the
  old value. Order is `Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear)` → configure
  → `Play()`, which is what every emitter builder outside `AreaFXRig` already did.
- **"Atomic" write with a shared temp name is neither.** `WriteSerializedJsonAtomic` used
  `<path>.tmp` — one fixed name for every writer of that file — so two overlapping writes
  opened the same handle and the loser threw `Access to the path is denied`. Writes DO
  overlap: `SaveService` chains its autosaves through `_pendingWrite`, but
  `SaveFileManager.WriteAutosaveAsync` starts a `Task.Run` that never joins that chain.
  It also did `File.Delete` then `File.Move`. Now: a GUID temp per write, plus a retrying
  swap (the existence check races the other writer either way round). Note the name lies —
  measured over 200 rewrites, `File.Replace` still left the target momentarily absent 3715
  times and delete-then-move 4327, because Mono's `File.Replace` is not Win32 `ReplaceFile`.
  What carries a run across a crash in that window is the rotating backups and the
  checksum. What temp+rename does buy is that a reader never sees a half-written save.
- **A persistence round trip is a pair.** Anything that writes a position/coordinate to
  `StreamingAssets/` must transform it the same way the loader untransforms it, and the context
  that transform depends on (zone, map slot, origin) must be resolved on BOTH sides. Spawners
  shipped for months writing absolute world coordinates into a field the loader read as
  zone-relative — they saved perfectly and came back 150 tiles away, once per restart. A test
  that exercises only one half proves nothing; assert the composition, and assert the shipped
  data is in bounds. See `.github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md`.
- **F10 Buildings save position-collapse bug** — root cause unknown but mitigated by 3 guards in `BuildingsRuntimeEditor.Persistence.cs`. If the F10 save ever logs `ABORTING save — ...`, that's this bug firing. Read `.github/incidents/BUILDINGS_SAVE_POSITION_COLLAPSE.md` for the recovery procedure and the next-step investigation checklist.
- **Never `Undo.RecordObject` in a bulk asset-import tool.** `BuildingPropImporter` created 193
  `BuildingTemplateData` assets and recorded each one for undo. They landed on the *global*
  editor undo stack, and the first thing that popped it — the EditMode suite, which exercises
  the runtime editors' undo — reverted all 193 IN MEMORY to their empty creation state while
  the correct data sat on disk. They then stayed dirty-and-empty, so the next `SaveAssets`
  would have written the emptiness over the good data. Symptom: `assetPath` reads `''` from
  `AssetDatabase.LoadAssetAtPath` while `File.ReadAllText` on the same `.asset` shows the real
  value. Use `EditorUtility.SetDirty` alone for data an operator re-runs rather than undoes.
  `BuildingTemplateOriginalScaleBackfill` still carries the same latent hazard.
- **A domain reload does NOT reload assets.** Recompiling reloads managed assemblies; the
  native `ScriptableObject`/`Texture` objects survive it with their in-memory values intact.
  Neither `AssetDatabase.ImportAsset(..., ForceUpdate | ForceSynchronousImport)` nor
  `EditorUtility.ClearDirty` re-reads an already-loaded asset either. When memory has diverged
  from disk, repair the object explicitly (parse the `.asset` and write the fields back) — and
  never reach for `AssetDatabase.ForceReserializeAssets`, which flushes the *bad* memory state
  onto the good file.
- **A placed particle emitter owns its configuration (copy-on-place).** A preset is a starting
  point, not a live link: an instance takes a copy of it when it is placed
  (`ParticleInstanceConfig`) and is independent from then on, so editing a preset reaches the
  NEXT placement and none of the existing ones. Before this, every row of the F1 properties
  panel edited the shared asset and an author tuning the emitter they had just clicked changed
  all eighty-four of them at once. The panel now edits whichever is in scope — a selected
  placement, or the preset when nothing is placed — and says which in its first header. The old
  coupling is available on purpose through "Reapply Preset → This / → All Placements".
  `particles_instances.json` is schema v4; a record from v1-v3 has no config and the loader
  freezes it against its preset as it loads, folding any v3 size ratios in, and WRITES THAT
  BACK once (`ParticleInstanceSerializer.SerializeRecords`, Editor only). The write is the
  half that makes the freeze real: in memory alone it lasts one session, so retuning the asset
  and restarting would re-snapshot every un-migrated placement from the new values — the
  coupling copy-on-place removes, coming back through the file. That writer emits the records
  it just read, verbatim and complete (including ones it could not spawn), with no scene scan
  and no coordinate maths, which is why it needs no anti-wipe guard.
- **Resizing a live emitter must not go through `ApplyPreset`.** It opens with
  `Stop(StopEmittingAndClear)` — correct when the effect is being replaced, catastrophic when a
  drag handle calls it every frame: every particle alive is destroyed sixty times a second, so
  a leaf field stops raining for as long as the author is resizing the box it falls out of, and
  takes a full lifespan to refill afterwards. `ParticleEmitter.SetOverrides` takes a live path
  (`ApplyGeometry`) that rewrites only what a size override can move — shape, `startSpeed`,
  gravity, velocity, noise, drag — on systems that keep playing. Anything the live path forgets
  is a module the emitter configures one way while being dragged and another way after a
  reload: `limitVelocityOverLifetime` was forgotten and diverged on 81 of 519 systems, because
  its LIMIT is derived from `speed`, which the reach override scales.
- **A per-instance size override can freeze an effect.** The reach ratio multiplies every
  motion term at once, so at its 0.05 minimum a leaf field's drift falls from 0.55 u/s to
  0.0275 — nine tenths of a pixel over a two-second life. The particles go on spawning and
  dying exactly as before, which reads as a broken emitter rather than a small one. The reach
  is not the only culprit: an orbit sweeps ground in proportion to the radius it turns around,
  so collapsing the EMISSION box freezes an orbital preset just as surely.
  `ParticleBoundsHandles.ClampToVisibleMotion` holds either drag back from that point and the
  status line says which knob does what the author meant.
- **Unity's particle bounds are not the particles.** `ParticleSystemRenderer.bounds` is built
  from position and size with per-particle ROTATION left out, and it trails the simulation by a
  step or two — measured, a spinning leaf pokes 20% of its size past the reported box and a
  fountain droplet at 4 u/s sits 3.5 cm outside it. `ParticleFootprint.OfLive` pads for both.
  Its analytic sibling has the opposite job (bound the worst case before any particle exists)
  and therefore over-reserves on purpose; the two are separate functions for that reason.
- **The noise module displaces far more than its authored strength.** `strength` behaves like a
  velocity against a scrolling field, so displacement grows with lifetime: measured across the
  44 noisy presets in the catalog, with drift and throw disabled, particles ended up as far as
  **3.67 x strength x lifetime** from where they started — the pollen haze wandering 4.4 units
  on an authored 0.22. Any bound over noise has to be shaped as `strength x life`, not as a
  constant, or it under-reserves by a unit and more on the long-lived hazes.
- **`NoiseModule.strengthX/Y/Z` are ignored unless `separateAxes` is set FIRST.** Without it
  the module reads the scalar `strength`, which defaults to **1** — so the old snow, authored
  as 0.4 horizontal and 0.05 vertical, was actually being shoved a full unit on every axis
  including up. Nothing warns; the flakes just do not fall the way the numbers say.
- **A weather effect is a STACK of systems, never one.** Every drop in a single-system
  downpour is the same size, brightness and speed, so the eye has no way to resolve distance
  and reads the whole thing as a decal on the lens — which is exactly how rain, snow and wind
  looked for as long as each was one `ParticleSystem`. Each now builds three to five
  `WeatherLayer` depth slices (far/mid/near, plus rain's ground splashes and haze, snow's
  settled specks, wind's leaves), and the near slices are deliberately large, faint and sparse.
- **Unity's stretched billboard aligns the quad's U axis with VELOCITY, so a streak texture
  must be WIDER than it is tall.** Rain shipped a 4x16 *vertical* strip in `Stretch` mode, so
  every drop was smeared across its own fall instead of along it. `WeatherTextures.Streak`
  draws horizontally and `WeatherEffectLayerTests` pins it.
- **A crosswind DISPLACES a curtain, it does not rotate it — and the displacement grows with
  time aloft.** Emitting from a slab exactly as wide as the viewport leaves the upwind third
  of the screen dry while everything piles up downwind. `WeatherEffect.LayoutFallingLayer`
  widens the slab upwind by the full drift, which then thins on-screen density by that same
  factor — so the rate is multiplied back up by `WeatherLayer.SpawnWidthScale`. Both halves
  are needed: widening alone makes turning the wind up look like the rain stopping.
  It also clamps the widening at 1.5 screens and shortens the lifetime to whichever edge the
  particle reaches first, because snow at ~1 u/s is airborne for 13 s and its honest storm
  drift is over a hundred units, nearly all of it off-screen.
- **Snow accumulation answers two questions with two mechanisms, and one alone is a colour
  grade.** WHERE is `SnowSplatMap`, a camera-following world-space R8 buffer that each
  expiring flake stamps with a soft additive disc; HOW it sits on a surface is the sprite's
  OWN alpha, walked up to six texels up to find the distance to open sky. The shader
  multiplies them, so the local depth sets how far the cap grows DOWN from the silhouette's
  top edge — a dusting is a one-texel crest, a deep drift creeps five or six texels down the
  roof. Before the map existed this was one global scalar, and no tuning makes a single
  scalar read as snow settling: it has no history, so there are no drifts, the wind piles
  nothing anywhere, and thawing is a slider going down instead of patches shrinking.
  Measured: with the buffer stamped at (5,5) and the global at 1.0, a wall there reads
  0.784 / 0.769 / 0.749 / 0.400 down from its top row while an identical wall 25 units away
  on unstamped ground stays at its bare 0.188 — and at global 0.2 the same drift is one
  crest row. Three constraints hold it up. Two live in the `.spriteatlas` files, not in the
  shader: `enableRotation: 0` is the only reason "up in texture space" is up in the world,
  and `padding: 2` is safe only because the distance is a MINIMUM over the samples, so the
  transparent padding always wins before a read can reach a neighbour. The third is the
  role, per material (`WorldSpriteMaterials.WorldWithSnow`): **Blanket** for Ground/
  FloorDecals, which face the sky across their whole area in a top-down projection, **Cap**
  for anything with a silhouette, **None** for entities. Getting Blanket onto a wall paints
  its whole face white and reads as a missing texture.
- **In Edit Mode a component whose `Awake` never ran never receives `OnDestroy` either.**
  Unity only calls Awake on a component added in Play Mode (or one marked `[ExecuteAlways]`),
  and it skips the matching teardown for the same reason — so a test that adds a component,
  destroys the object and asserts on what teardown released is measuring nothing at all, and
  passes or fails for unrelated reasons. `SnowSplatMap` pairs `EnsureBuilt()` with
  `ReleaseBuffer()` for exactly this: Play Mode reaches them through Awake/OnDestroy, tests
  call them directly. Related, and it bit in the same hour: `Destroy` is an outright ERROR in
  Edit Mode, so a singleton guard that destroys the loser must instead leave it inert.
- **A landing is an expiring particle, never a collision.** Unity's particle collision works
  and would be confidently wrong here: a building's collider is its FOOTPRINT while its
  sprite is drawn rising above it, so colliding flakes pile along the base of every house
  instead of on its roof. The randomised lifetimes already stop flakes at a spread of
  heights, and the per-sprite alpha cap is what decides a landing over a roof sits ON the
  roof. Related: `SnowAccumulation.SetAmount` has to fill the map as well as set the scalar,
  because the two multiply — `snow 1` over an empty buffer would change nothing on screen.
- **A static reset the Domain-Reload ratchet accepts is `stsfld` or `field.Clear()`, nothing
  else.** `DomainReloadStaticResetTests` reads the hook's raw IL, so
  `System.Array.Clear(_cache, 0, n)` — which passes the field as an ARGUMENT — counts as no
  reset at all and fails the suite. A `static readonly` array cache therefore cannot be reset
  in a way the scanner recognises: drop the `readonly` and assign a fresh array.
- **Weather particle materials are unlit, so the day/night Global Light 2D reaches none of
  them.** Midnight rain renders at noon brightness over a world at a few percent of it. The
  cycle's colour is folded into each layer's START colour (`WeatherLayer.SetTint`), which is
  the one point `colorOverLifetime` multiplies through. Snow takes far less of it than rain
  (`AmbientResponse` 0.35-0.55 vs 0.95): a snowfield is the brightest thing in a night scene,
  and a flake tinted down to the ambient simply vanishes.
- **`ParticleSystem.Simulate` PAUSES the system it advances, and a system that has never played
  swallows `Emit`.** Both bite in EditMode tests: without a `Play()` before each `Simulate` the
  second step of a test measures a frozen emitter and passes for the wrong reason, and a probe
  that emits before ever playing measures nothing at all. `Emit(count)` goes through the shape
  and applies `startSpeed`; `Emit(EmitParams)` with an explicit zero velocity does not, which
  once made half the catalog look motionless.
- **A clean console is not a successful compile.** Unity defers compilation while in Play Mode,
  so a broken script can sit in the working tree with `read_console` returning zero errors and
  every subsequent `execute_code` running against the STALE assembly. After any C# change,
  confirm the new code is actually loaded — `typeof(X).GetMethod("NewThing") != null` through
  `execute_code` — before trusting a measurement or a green console.

- **A world swap is not a tile repaint.** `WorldGridBuilder.ClearWorld` calls
  `ClearAllTiles` on the tilemaps and destroys NOTHING else, so every placed building, light,
  spawner and particle emitter — and each building's per-cell `BoxCollider2D` — survived into
  the swapped-in overlay and floated over it, walls and all. `WorldTransitionService` is now
  the single owner of the swap and tears world content down through
  `MapEditorManager.ClearAllSpawnedWorldContent` / `ReloadAllWorldContent`, the same public
  entry points `reloadworld` uses. `ZonePortal` delegates to it; so does `BuildingDoor`.
- **Clearing the world before validating the destination is not transactional.**
  `OverlayLoader` logs and returns on a missing or malformed file, which — after the world had
  already been cleared — left the player standing in a black void with no way back. Check
  `WorldTransitionService.IsOverlayLoadable` FIRST; the F10 Door panel and the `door` console
  command refuse an unloadable target at author time for the same reason.
- **`ZoneManager.ForceZoneName` survives exactly one frame.** `Update` re-detects a zone from
  the player's position against the BASE-WORLD zone list every frame, so an interior loaded at
  (0,0) is immediately re-labelled with whatever outdoor zone overlaps it, taking the music and
  ambience with it. Use `SuspendDetection` / `ResumeDetection` around any overlay that is not
  part of the zone database.
- **An editor autosave while the world is torn down persists the emptiness.** Every runtime
  editor force-saves the scene on each edit, and inside an interior the scene legitimately
  holds no buildings, lights or emitters while the files hold hundreds. Count-based anti-wipe
  guards read that as "the author deleted everything" — it cost 188 placed particle emitters
  once. `WorldTransitionService.IsBaseWorldContentSuspended` states the fact instead of
  inferring it; `RefuseWorldContentWrite` is checked by the Buildings and Particles save paths.
- **A doorway anchor is normalized, never a collision-grid glyph.**
  `BuildingCollisionLoader.ResampleGrid` collapses each destination cell to one bool by OR-ing
  its sources, so a `D` glyph in that matrix is erased the moment the instance carries a
  `scale` override — silently, on exactly the buildings a designer resized. `hasDoor` +
  `doorOffsetNormalized` + `doorSizeNormalized` live on the template (where the ART has a
  door); `overrides.door` lives per instance (where THIS house leads).
- **A doorway is detected by polling, not by a trigger.** Buildings carry no `Rigidbody2D`, so
  a trigger depends entirely on the player's Dynamic body — and a Dynamic body that comes to
  rest goes to sleep (`Player.prefab`: Sleeping Mode = Start Awake, Time To Sleep = 0.5 s). A
  SLEEPING BODY STARTS NO NEW CONTACTS. `ResurrectionZone` already polls for the same reason.
  Every teleport also zeroes `velocity` and calls `WakeUp()`.
- **A pack whose art defeats `analyze_tile_edges.py` is not a broken pack.** That tool
  labels a probe by clustering the sheet's palette into materials and demanding one own
  >=65% of it, which works on flat-shaded art. `rock_lava` is not flat-shaded — the rock
  carries two greys and the lava four oranges — so the clustering splits ONE terrain across
  several materials, no probe reaches the purity floor, and the verdict is
  `UNRELIABLE - 81% of edges too blended`. The art was fine: rock and lava separate cleanly
  on red-minus-blue alone (a pure-lava cell scores 10.2% rock, a pure-rock cell 100.0%, with
  nothing between). `tools/atlas/wave2/rock_lava_ruleset.py` uses that two-class classifier,
  cross-checks four probe geometries against each other, and merges the pack into
  `tile_rulesets.json`; the general analyser is left alone. Run it AFTER the analyser, which
  rewrites that file wholesale.
- **A 256x256 island render can be a complete Corner16 pack.** `rock_lava.png` looks like a
  preview, not a sheet — but cut on the 32 px grid it yields exactly 16 distinct cells out
  of 64, and those 16 map one-to-one onto the 16 corner signatures. Check for that before
  assuming a render has to be hand-cut: the 32 px column self-difference was the lowest of
  every candidate period, which is the tell that the island was drawn on the grid.
- **Two Corner16 packs cannot share a primary terrain name.** `FindPaintRuleset` resolves a
  terrain NAME to exactly ONE ruleset — highest `Priority`, ties by list order — so a second
  pack claiming `rock` is simply unreachable from the F8 auto-brush, silently. `rock_lava`
  therefore paints as **`stone`** (pale loose rubble, #848484) while `rock_water` keeps
  `rock` (smooth dark, #3c3c3c). Check `FindPaintRuleset(primary)` actually returns the new
  pack before calling an import done.
- **`TilesetRulesetImporter` leaves a NEW ruleset unnamed unless the JSON names it.**
  `TerrainTileResolver.ResolveVariantForCell` keys corner slots by `TerrainSecondary`, so a
  ruleset with empty terrain names imports 16 clean slots and auto-tiles nothing. A pack
  entry may carry `terrainPrimary`/`terrainSecondary`; names already on an asset always win,
  so a re-import never overwrites a hand-checked pair.
- **An entity gets more than one attack through a variant LIST, never a new `AnimState`.**
  The seven states are enumerated positionally in four independent places — `EntityAssetConfig`'s
  own fields, `DirectionalAnimator`'s seven serialized sets plus seven accessors plus its
  seven-argument `SetSpriteSets`, the `GetSpriteSet` switch, and `EntityAnimationBinder`'s
  build-and-fallback chain — so an eighth state pays that tax four times and again for the
  ninth. `EntityAssetConfig.attackVariants` pays it once. It also keeps `AnimState` untouched,
  which matters because `PlayerController.Movement` gates locomotion on an Idle/Walk/Chase
  whitelist and reverts on a Cast/Attack one: a new enum value missing from the second list is
  entered and never left, and nothing else rescues it. A variant INDEX under the existing
  `Attack` state inherits both whitelists by construction, and `FSMMonsterBrain`'s state-type
  switch (with its silent `_ => Idle` default) needs no edit at all.
- **A variant change that is not also a state change is swallowed.** `SetState` early-returns
  when neither state nor direction changed, so swinging twice in the same direction with a
  different animation silently keeps playing the first one's frames. The three-argument
  overload counts a changed variant as a change; `RestartCurrentState()` covers the
  same-variant re-swing, which `AttackState` reaches without ever leaving the state.
- **`AttackState` used to cut its own animation off.** `_attackDuration = windup + 0.3 s`
  against a global `frameInterval` of 0.15 s means an eight-frame swing (1.2 s) was dropped at
  frame four, mid-arc. `BeginSwing` now takes the LARGER of that historical floor and
  `GetStateLength`, so the 18 monsters with a one-frame attack pose are paced exactly as
  before.
- **Retiming an attack animation retimes its DAMAGE.** The melee cooldown bounds the hit rate,
  it does not hold it steady: exactly one `TryAttack` is attempted per swing, at the windup, so
  the realised interval is the swing period rounded up to the next multiple that clears
  `meleeCooldown`. Measured on `knight_red` (windup 0.45, cooldown 1.1): a 0.75 s swing attempts
  at 0.45 / 1.20 / 1.95 and lands at 0.45 / 1.95 — one hit every 1.5 s, every other attempt
  refused. A 1.2 s swing attempts at 0.45 / 1.65 / 2.85 and lands all three — one every 1.2 s.
  Lengthening the animation to stop it being cut raised that monster's melee DPS ~25 %. Re-check
  `meleeCooldown` whenever an attack's frame count changes.
- **Measure a swing AFTER turning to face the target.** `GetStateLength` reports the frame
  count of the animator's CURRENT direction, so calling it before `FacePlayer` sizes the swing
  against whichever way the entity happened to already be facing. Invisible on a uniform 8x8
  sheet and obvious on one whose direction buckets differ: measured, the first swing came out
  0.5 s instead of 1.2 s, and only the first.
- **A direction-only change re-renders through `RefreshCurrentFrame`, which must be handed the
  active attack variant.** It resolves the set itself rather than reusing the cursor, so calling
  the parameterless `GetSpriteSet(state)` there falls back to variant -1 and flashes one frame
  of the DEFAULT attack into the middle of a variant — every time a strafing player crosses a
  facing sector, hidden again by the next tick.
- **`castSheets` is dead weight on a monster with no spells.** `NPCCastState` is entered ONLY
  by `NPCAutoCast`, and `EntitySetup.ConfigureMonsterAutoCast` returns immediately when
  `autoCast` is false — it does not even add the component. A cast animation authored on a
  melee-only monster never renders a single frame. `knight_red`'s shield bash sat there until
  it was moved into the attack rotation.
- **`animation_map.json` reaches no runtime code.** The F12 Animations panel and
  `FSMSeedGenerator` write it; `FSMMonsterBrain.OnFSMStateChanged` hardcodes the same mapping
  in a C# type switch, and each state class re-asserts its own `AnimState` every frame on top
  of that. Editing the file changes nothing in game. The two writers also disagree on the key:
  the seed generator emits `per_set`, the runtime editor reads and writes `by_set`.
- **`DirectionalAnimator` never flips a sprite, so a side-view character needs its mirrors
  baked.** `ChaseState` says so in as many words ("flipX would corrupt directional
  sprites"), and `PlayerController` only sets `flipX` when there is no animator at all.
  `CreateSetFromLinearFrames` slices a linear list into eight CONTIGUOUS per-direction
  buckets (S,SE,E,NE,N,NW,W,SW) — it is not an animation, it is eight animations end to end.
  Feeding it a single 8-frame side cycle therefore gives one static frame per direction. The
  knight ships 8 frames plus 8 pre-mirrored copies per state, referenced 64 times to fill the
  eight buckets.
- **Trimming an animation frame tight to its own alpha breaks it.** `slice_prop_sheet.py`
  does exactly that, which is right for a prop and wrong for a cycle: the cape and sword move
  the bounding box every frame, so the walk jitters and the feet leave the ground.
  `tools/atlas/wave2/build_knight_frames.py` reuses that tool's segmentation but pastes each
  frame onto one shared canvas, anchored on the CELL centre (anchoring on the body would
  cancel the hip sway and the lunge) and on the row's lowest BODY pixel — taken from the
  largest connected component, because a torn-off cape tip sits below the boots and would
  drag the ground line down with it.
- **Everything directly under `StreamingAssets/Maps/` is a 50x50 zone tile** — `WorldLoader`
  composes them by offset and `RealShippedOverlayBoundsAndNamesTests` asserts that size for
  every file it finds there. Interiors are rooms of arbitrary size and live in
  `Maps/Interiors/`; generate one with `tools/maps/generate_interior_overlay.py`.
- **`<Keyboard>/e` is bound TWICE** in `ValkurInputActions.inputactions` — to both `Interact`
  and `SpellSlash` — and nothing reads `InputService.Gameplay.Interact`.
  `NPCInteractable.Interact()` has no caller either, so vendors' `OnInteract` never fires. Any
  feature that wants a key-press interaction has to resolve that binding first.
- **`Sprite-Unlit-Default` declares no `_SrcBlend`/`_DstBlend`, so every blend-mode write
  against it is a SILENT no-op.** `ElementalSprites.SharedUnlitMaterial` is built on that
  shader, and `SetInt("_SrcBlend", One)` on it compiles, logs nothing, and leaves the
  surface on fixed alpha — `BeamMaterialCache` records the same measurement. On alpha the
  brightest pixel a "glow" can produce is its own colour, so a mid-value lilac core cannot
  blow out and a wide faint halo is a net luminance LOSS over pale ground. There are two
  correct additive paths and they are not interchangeable: `ParticleMaterialCache.Get(tex,
  additive: true)` (URP/Particles/Unlit) for a `ParticleSystemRenderer`, and
  `ElementalSprites.SharedAdditiveMaterial` (`Valkur/SpriteAdditive`) for a `SpriteRenderer`.
  Both are `SrcAlpha/One`, not `One/One`, so alpha still modulates brightness and a fade
  actually fades. Related: a material handed to a `ParticleSystemRenderer` must carry its own
  texture — a `SpriteRenderer` supplies one, a particle renderer does not, so the untextured
  shared material draws hard white SQUARES, and `AuraController.cs:262` writes
  `sharedMaterial.mainTexture` through that same global static, so casting a healing aura
  retextures every other effect pointed at it.
- **A persistent spell effect has five exit paths and only `OnDestroy` is on all of them.**
  Its own timer, eviction by `maxInstances`, a zone change, its caster dying, and scene
  unload — the last four go through `SpellEffectRegistry`'s `Object.Destroy`, so a fade the
  effect implements on its own timeline is simply skipped, and by the time any of its code
  runs the object is already doomed. That is not the edge case: `arcane_flame` runs 5 s on a
  2 s cooldown, so in normal play every instance but the last is EVICTED and the hard cut is
  what the player sees, roughly every two seconds. `ISpellEffectDissipates` is the seam —
  `DestroySafely` offers ownership before destroying, and because the handle is dropped first
  a dissipating effect stops counting against `maxInstances`, so the recast that evicted it is
  never refused. The zone-change path passes zero on purpose: the world it was drawn into is
  being torn down underneath it. Note also that a rig built in `Initialize` renders ONE frame
  before `Update` first runs, so an ignition ramp has to be seated at the end of `Initialize`
  or the effect pops at full alpha for 16 ms before starting to fade in.
- **An energy charge is a COLUMN, and nothing here drew one.** `AreaFXRig` is a disc and
  `IceWallVisual` is a line; a ki charge is tall, anchored to a body that can walk, and its
  silhouette FLICKERS — which is the one thing that separates fire from a light. `KiAuraFX`
  is seven layers: a smooth column, flame tongues that carry the flicker, a haze, sparks
  streaming upward, opaque ground debris (the only non-additive layer, and the only one that
  says the world is being affected rather than just lit), ground pulse rings, and lightning.
  Its sorting is rebased on the CASTER's own order every time that changes — `YSortEntity`
  rewrites their order whenever they walk, so a value captured once at build time pops the
  aura in front of the character the first time they take a step.
- **Intensity is not a size.** The seven shipped charges (`charge_ki_spirit` … `_void`) run
  0.15 to 1.00 on `SpellDefinition.scale`, and what that dial moves is DENSITY and BEHAVIOUR:
  6 to 15 flame tongues, 35 to 130 sparks a second, no ground debris at all below 0.32, no
  lightning below 0.60, ring pulses every 2.1 s down to every 0.34 s. Height barely moves
  (3.5 to 4.5 units on a 2.5-unit body) because a calm charge should read as CALM, not as
  small. Measured: the first height values tried put the void charge at 9.8 units against a
  10-unit-tall camera, so the aura filled the screen and stopped reading as something coming
  off a person.
- **A palette derived from one swatch cannot be authored wrong.** `KiPalette.From` takes the
  spell's `particleColor` and derives core / mid / edge / light from it, so a designer picks
  one colour and gets a coherent aura — and it is impossible to author one whose core is
  darker than its edge. The edge is DEEPENED in HSV rather than multiplied, because plain
  multiplication desaturates towards black and turns a crimson aura's edge grey, when the edge
  is supposed to be the most colourful part of it. Note `particleColor` holds opaque white
  when nobody has touched it, which is indistinguishable from a deliberate white; the fallback
  is a pale blue-white, so a designer who meant white gets very nearly what they asked for.
- **A cast pose is not a cast.** At 16 PPU a character is forty pixels tall and the
  difference between their idle frame and their cast frame is a few of them, so for as long
  as the game existed every spell's only visible event happened somewhere ELSE — a projectile
  leaving, a wall rising three units away. What reads as casting is LIGHT gathering on the
  body and then leaving it. `SpellCastFlourishFX` is that, in three beats: a ground sigil, a
  ring of motes that gathers, and a release. It fires from `SpellCaster.ExecuteSpell` — the
  single seam every cast passes through, monsters included — and refuses exactly two spell
  types: `WeaponLoadout` (whose `WeaponSwapFlashFX` owns those frames) and `AnimationProbe`
  (whose whole job is that the animation can be SEEN). That carve-out lives in `AppliesTo`
  rather than inline, because in Edit Mode a test cannot tell it apart from the
  `Application.isPlaying` leak guard.
- **Colour and gesture are two axes, and folding them together costs the effect its meaning.**
  `ElementPalette` answers "what element is this"; `CastFlourishProfile` answers "what is the
  caster DOING". They are genuinely orthogonal — an ice wall and an ice bolt are the same blue
  and nothing like the same gesture, while a summoned totem and a summoned wall are different
  colours and the same gesture — and the first version of the flourish had only the first, so
  all 47 shipped spells cast identically and differed by hue alone. Nine families, dispatched
  on `SpellDefinition.type` the way `SlashProfile` dispatches on arc: **Hurl** spirals in and
  throws forward, **Edge** strikes sparks off the swing arc and draws no circle at all (a cut
  summons nothing), **Conjure** EXPANDS its circle while motes fall out of the sky (power being
  laid down, not taken in), **Invoke** lifts motes off the floor and throws them at the sky,
  **Ward** orbits the body and never lets go, **Surge** leaves its motes BEHIND, **Vanish**
  implodes with no burst and no lance, **Channel** breathes and holds. **Vortex** is the only one with a
  SILHOUETTE rather than points of light — a funnel narrow at the floor and flared at the
  top, and `forceMode` reverses both its spin and its debris, which is genuinely the whole
  difference between `vortex_pull` and `vortex_push`. Each family then sizes
  itself off the spell's own data, so two slashes with different arcs throw different numbers
  of sparks. Measured live on eight spells: Hurl ends +1.10 forward, Surge -0.87 (behind),
  Invoke +1.30 up, Conjure starts +2.82 ABOVE the hand, Vanish ends at radius 0.00.
- **`wallWidth` / `wallHeight` are WORLD UNITS, and used not to be.** `WallExecutor` divided
  both by 32 — a leftover from the Python build, where they were pixels — so the shipped
  `wall_ice` (12.5 x 3.125) resolved to a barrier **0.78 units wide and 0.049 tall**, collider
  included: twelve screen pixels by less than one. Nothing failed; the wall simply was not
  there, and the wrongness was internally consistent everywhere except on screen. The tell was
  that the executor's own fallbacks (6 x 1.5) were thirty times larger than anything the asset
  could produce. `IceWallGeometryTests` now asserts the composition rather than either half,
  the way the spawner-drift note above prescribes.
- **A radial rig cannot draw a LINE.** `AreaFXRig` is four concentric discs plus a circle
  emitter — right for a puddle or a vortex, and what the ice wall used for years: stretched
  onto the barrier's quad the discs became ellipses, the particle emitter became a point in
  the middle, and the `Light2D` hanging under the scaled root rendered at some other radius
  entirely. `IceWallVisual` is the line-shaped answer: an UNROTATED, UNSCALED root, crystals
  placed along the axis by POSITION (so they stand up on screen whichever way the wall runs),
  absolute per-child sizes, a Box emitter as long as the wall, and lights spread along it.
  Only the collider child is rotated.
- **`refresh_unity(scope="scripts")` does not reimport a `.asset` edited on disk.** Editing
  spell data with `sed` and refreshing scripts leaves Unity holding the OLD ScriptableObject:
  measured, `vortex_push` read back `spawnAtMouse=False followCaster=True range=0` from memory
  while the file on disk plainly said `1 / 0 / 10`. It is silent, and it is worse than the
  known "a domain reload does NOT reload assets" trap because it fools BOTH probes —
  `AssetDatabase.LoadAssetAtPath` returns the in-memory object, so an EditMode test asserting
  on the shipped data measures the stale copy too and passes or fails for the wrong reason.
  Use `scope="all"` after any data edit, and confirm by reading the file's own text back
  beside the loaded object.
- **Two spells that differ in EFFECT should not also differ in DELIVERY.** `vortex_push` rode
  its caster while `vortex_pull` was thrown out in front, so they were two spells with two
  control schemes and the actual difference — which way the force points — was the hardest
  thing to notice. Both are cursor-placed and both drift now; `forceMode` is the whole
  difference, which is what the spin direction, the debris direction and the streak direction
  have been saying all along. `followCaster` still works and no shipped spell uses it, so its
  test builds a synthetic caster rather than asserting on an asset.
- **`spawnAtMouse` did not read the mouse.** All three executors that honour it resolved the
  same thing — `castStart + direction * someFixedDistance` — so the flag only chose WHICH
  constant offset to use. A vortex aimed at a target two units away landed six units past them
  and one aimed across the room landed in exactly the same place. Nothing failed; the field was
  internally consistent and simply did not mean what it says, the same shape as `vfxPreset` on
  a vortex and `animation_map.json`. `SpellTargeting.ResolveGroundTarget` is the single owner
  now, and two of the three were ALSO dividing `range` by 16 — the Python pixel scale, a fourth
  sighting. Three constraints hold it up: the cursor comes through `MouseInputManager` (never
  `Mouse.current`), it is refused for any caster not tagged `Player` because a monster has no
  pointer and must keep aiming with its facing, and the result is clamped to the spell's own
  `range` so the reach stays something a player can learn. Author that `range`: leaving it 0
  hands the cast distance to a constant inside the executor.
- **A hard-coded `||` beside an authored flag makes the flag unfalsifiable.**
  `VortexFieldExecutor` read `if (ctx.Spell.spawnAtMouse || isPull)`, so clearing the box in F4
  changed nothing for half the spells that carry it — the panel showed a control that could not
  do anything. Worse than a wrong default, because the data and the screen disagree while both
  look right. Same family as `FindBaseRuleset` excluding every Corner16 pack.
- **Moving a call one level down breaks a source-scanning test that was RIGHT to break.**
  `CastOriginContractTests` greps each executor for `ResolveCastStart(`; routing three of them
  through `SpellTargeting` left the guarantee intact and the grep looking at the wrong file.
  The fix is to point the fixture at the new owner, never to re-inline the call to satisfy it —
  listing the executors would demand a call they no longer make while leaving the helper they
  now all depend on unguarded.
- **A stationary effect gives itself away in proportion to how long it lives.** At two seconds
  nobody looked at the vortex long enough to notice it was a spinning decal bolted to one spot;
  at eight it is the first thing the eye reports. `VortexFieldController.Drift` tracks it across
  the ground at 1.15 u/s on a heading INTEGRATED from smooth noise — sampling a direction per
  frame gives a shape that vibrates in place, integrating one gives a curve it commits to and
  leans out of. Measured over a full life: 9.22 units of path, never more than 3.45 from where
  it was cast, heading passing through 253-246-310-38-24 degrees. The leash is not optional —
  8 s at 1.15 u/s is nine units, and without it the spell walks out of the fight.
- **A rig whose every child shares one velocity is a decal being dragged.** Two things fix it
  and they are different statements: the flared top LEANS with travel (and the neck does not,
  because the neck is what is touching the ground), and the torn-up debris LAGS, because ground
  thrown into the air is no longer attached to the thing that threw it. Measured at 1.15 u/s
  east: lean at the top 0.34 -> 0.86 units, mean debris x 0.03 -> -0.38. The lag mechanism also
  serves `followCaster` for free — there the velocity is the PLAYER's, so a running caster tows
  a plume behind their own vortex.
- **The cone's surface needs exactly one owner.** The bands draw it, the dust and debris ride
  it, the discharges crawl along it — so `WallOffset(height01)` is shared by all four. A lean
  the bands know about and the arcs do not is a bolt hanging in the air beside the shape it is
  supposed to be attached to, and that is invisible in code and obvious on screen.
- **Two constants holding the same number in two files is a desync with a delay fuse.**
  `SPIN_UP_SECONDS` lived in both `VortexFieldController` (which ramps the FORCE) and
  `VortexFunnelFX` (which ramps the FUNNEL), both at 0.40. Nothing failed while they agreed;
  raising one for the eight-second field would have started grabbing enemies out of a hole in
  the ground. It is `VortexFunnelFX.SpinUpSeconds` now, and the controller reads it.
- **Lengthening a control field is a BALANCE change, not a timing one.** Both vortices went from
  2 s to 8 s on an unchanged `cooldownDuration: 2`. With `maxInstances: 1` a cooldown shorter
  than the duration means the player always has one out AND can evict their own to reposition
  it, so eight seconds of hard crowd control lands as permanent crowd control. Cooldown 12,
  mana 25. `VortexFieldTests` fails if a cooldown ever drops below its own field's duration.
- **An effect made only of CONTINUOUS motion stops being read after about a second.** Bands
  turning, debris circling, streaks running — all at a steady rate — and the eye files the
  whole thing as one texture. What resets it is an EVENT: `VortexFunnelFX`'s discharges climb
  the funnel wall, appear, and are gone. The duty cycle is the entire design, and the first
  interval tried (0.16-0.52 s across three arcs) measured **78 % of frames lit**, which is not
  lightning but a lamp with a flicker — it forfeits the one thing the layer is for. Shipped at
  0.45-1.30 s: 30 % duty, six or seven distinct strikes per two-second cast. The arcs also run
  along the CONE rather than through it, sharing `WallPoint` with the bands, because a bolt
  across the middle says the column is solid when the whole silhouette says it is hollow.
- **One opaque layer is what separates "affecting the world" from "lit".** Every other piece of
  the vortex is additive light; the ground debris is `Sprite-Unlit-Default` and deliberately
  dark. It cannot be folded into the shared additive material as a tidy-up — a dark chip on an
  additive surface adds almost nothing, so the layer would vanish with nothing failing.
  `KiAuraFX` records the same rule for its own ground debris, and `VortexFieldTests` now pins
  the material contrast in both directions.
- **A ground-plane layer is squashed by ONE parent, never per item.** A suction streak points
  along a radius, so squashing each streak individually foreshortens its LENGTH without turning
  its direction and it slides across the floor instead of lying on it. One `GroundPlane`
  transform at `(1, 0.34, 1)` with the rotation on the children — the same split the bands use,
  and the reason the rotation must be a CHILD of the squash rather than share a transform with it.
- **A funnel that TRACKS walks over people, so its neck has to be a hole.** Measured, the
  radius at chest height is 1.17 units against a 0.9-wide character, so whoever it crosses is
  inside it, and eighteen additive bands summing to 3.98 paint them out entirely.
  `NECK_CLEAR_HEIGHT` fades the lowest fifth. This surfaced through `followCaster`, which
  parked one specific caster in the neck permanently; both vortices are cursor-placed and
  drifting now, which turns it from one spell's problem into everyone's.
- **A snapshot is not a travel range, and it fails the correct implementation.** A test asserting
  that push debris clears its rim read `|localPosition.x|` on one frame — but a chip at its
  furthest and a quarter turn round contributes almost nothing on the x axis, which is the only
  axis the ground squash leaves alone. It reported 3.28 against a 3.8 rim on code that really
  does reach 4.17. Sample the extreme OVER a run when the quantity is a range rather than a state.
- **Two spells that are each other reversed must COVER THE SAME GROUND, or one of them is
  quietly the worse-looking one.** `vortex_push` threw its ground streaks to 1.39x the force
  radius and its debris to 1.25x while `vortex_pull` worked between the rim and the neck, so
  the same sixteen streaks and eighteen chips spread over 46 % and 29 % more floor. Three
  things follow from one constant and all three read as "cheaper": the layer is sparser, it
  moves faster in world units for an unchanged cycle rate, and a third of it lands outside the
  circle the ground ring exists to promise. Reported as "pull looks much better animated than
  push, independent of the colour", which is exactly right. `GROUND_REACH` is now one constant
  for both directions and the two runs are each other backwards — measured at radius 3.7, span
  3.40 either way and drift -76.74 against +76.74 milli-units per frame.
- **Direction is the SIGN OF TRAVEL, never how far out something gets.** The test that was
  supposed to prove push throws outward compared absolute reach, so it passed only because of
  the asymmetry above — it was pinning the defect as if it were the signal. Sample the
  per-frame change instead, and drop the wraps, or a looping layer averages to zero and every
  direction assertion passes for the wrong reason.
- **Thickness is a brightness dial too, and it has a texture-edge limit.** Doubling the
  tornado bands' weight doubled the screen area one band covers — measured x2.00 — so on the
  additive material the column arrived twice as bright, exactly as raising the band COUNT does;
  `BAND_AREA_COMPENSATION` halves the per-band alpha to keep the tuned total, and the gather
  needs the same factor because it draws the same sprites. The second half is geometric: a band
  reaches `BandRadius + thickness` and the sprite's normalized space stops at 1.0 on the axes,
  so doubling thickness at `BandRadius = 0.82` would have run to 1.03 and sliced the ring flat
  at the four cardinal points. What sets the drawn weight is the RATIO `thickness / BandRadius`
  — the sprite is scaled until its line lands on the wanted world radius — so both numbers had
  to move together and neither means anything alone.
- **A single frame cannot measure a quantity that is spread over angles OR over time.** It bit
  twice in one session, in two disguises: a debris travel range read from one frame (a chip at
  its furthest but a quarter turn round contributes almost nothing on the x axis), and a mean
  offset read from one frame (eighteen chips at random angles put about a third of a unit of
  noise on the mean, against a lag of six tenths — it passed on the draw and failed the moment
  an unrelated constant moved). Average over the run.
- **On an additive stack, the band COUNT is a brightness dial.** A pixel receives the SUM of
  every layer over it, so raising `VortexFunnelFX.BANDS` from 9 to 18 did not make the funnel
  finer — measured, the summed band alpha went from 3.99 to **7.97**, and a red vortex washes
  out to white through the middle of its own column, which costs the spell the one thing
  separating it from the blue one at a glance. Per-band alpha is divided by
  `BAND_ALPHA_REFERENCE_COUNT`, so more bands buy RESOLUTION (the vertical slice went 0.66 u
  to 0.33 u) and not light. The same arithmetic applies to any additive rig whose layer count
  is tunable — `ShieldSphereFX`'s facets, `KiAuraFX`'s tongues.
- **A sorting order computed from an index must be bounded by a DERIVED constant.** The funnel
  gives band `i` order `ORDER_BAND + i` so higher bands draw over lower, and the near-side
  debris has to clear the whole stack. `ORDER_DUST = 72` was right at 9 bands and silently
  wrong at 18 (the stack reaches 75), which sinks the front-side scraps behind the funnel and
  costs the rig the only statement it makes about depth. It is `ORDER_BAND + BANDS + 2` now.
  Same failure shape as `SpriteTintStack`'s hand-maintained `LAYER_COUNT`.
- **A test that names a child by index stops testing what it says it tests.** `Band8` was the
  TOP of a nine-band funnel and the WAIST of an eighteen-band one, so the cone-taper assertion
  would have gone on passing while measuring the wrong band. Name it off the rig's own
  `BandCount`.
- **A radial disc cannot draw a FUNNEL either, and a vortex is the third spell to be
  authored in Python pixels.** `vortex_pull` / `vortex_push` shipped `radius: 17.5` — the
  number that was right in the build this game was ported from, after `wallWidth` and the
  totem's `radius / 16`. Measured live against a camera 33.33 x 16.67 world units: the halo
  drew **32.4 units wide (97 % of the screen)**, the emitter's shape radius came out at
  **122.5 units**, each particle was **1.75 units** across, and the `Light2D` rendered at an
  effective **367 units** — eleven screen widths of violet. `Physics2D.OverlapCircleAll` swept
  a 35-unit circle, so enemies were dragged in from off-screen. Nothing failed; every number
  was internally consistent and disagreed only with the display. `VortexFunnelFX` replaces
  `AreaFXRig` here for the same reason `IceWallVisual` replaced it for the wall — four
  concentric discs can never show the one thing the spell is named after, its ROTATION — and
  the two spells now author 3.6 / 3.8, measured at 28-29 % of screen width.
- **Three of those 367 units came from scaling the ROOT, and the light is what pays.**
  `VortexFieldController` did `AreaFXRig.Attach(transform, palette, radius)` and then
  `transform.localScale = Vector3.one * radius`, so every child was sized twice and the
  `Light2D` hanging under it rendered at `authored x lossyScale`. `PuddleController` and
  `TotemController` still carry the identical two lines; their radii are small enough that
  it has not surfaced. A rig that wants a world size gives its children ABSOLUTE sizes and
  leaves its root at identity — `VortexFunnelFX` does, and `VortexFieldTests` pins it.
- **The silhouette of a funnel says nothing about where the force reaches, so something else
  has to.** A tornado is narrow where it touches down and flared where it opens — which is
  the shape a player reads as a vortex, and it is upside down with respect to the physics: the
  widest part is metres above the ground circle `OverlapCircleAll` actually queries.
  `VortexFunnelFX` draws a ground ring pinned to that circle through `ElementalSprites.Ring`'s
  0.78 band (`span = radius / 0.39`) and pulses it in BRIGHTNESS only — a circle that breathes
  in size is a promise that moves.
- **A vortex force needs a rim grip, a swirl and a speed clamp, and each is a separate way to
  look broken.** Every NPC body in the project ships `mass 1, drag 0`, so `AddForce` integrates
  without bound: measured, the old pull peaked at **39 u/s** and slung a body from +16.6
  straight through the centre to **-12.6**, out the far side. The three fixes are independent.
  A linear falloff that reaches 0 at the rim means the spell cannot GRAB — at 95 % of the
  radius it kept 0.05 of the force and drifted half a unit in two seconds (`RIM_GRIP`).
  A purely radial aim is what does the slinging (`PULL_SWIRL`). And the tangential term hands
  the body orbital momentum that nothing removes, so a "pull" captures an enemy into a stable
  orbit at 2.0 units and never gathers it (`FIELD_DAMPING`). Shipped behaviour, measured:
  three bodies released around the rim close from **5.92 units apart to 1.61**, all inside a
  third of the radius by 0.89 s; push spreads them from 6.25 to 16.50.
- **A force model that reads `Time.deltaTime` cannot be measured.** Driving `ApplyForce`
  through `Physics2D.Simulate` from `execute_code` gave `Time.deltaTime = 0.0016` — a tenth
  of a real frame, because nothing is rendering — so the first three tuning passes understated
  the force tenfold and each "fix" was measuring the harness. `Time.captureDeltaTime` does not
  help: it takes effect on the NEXT frame, so within one call it reads back unchanged. Pass
  the delta in as a parameter; the rig's `Tick(deltaTime, ...)` already did.
- **A workaround outlives the bug it was written for.** `CastFlourishFamilies.Vortex` sized its
  gather as `radius * 0.11` with a comment saying `radius` is "authored in the legacy pixel
  scale (17.5)". Once the two spells authored real world units that factor clamped every vortex
  to the same 1.15 minimum, so the dial stopped working — silently, since the clamp is the
  correct-looking end of the range. Grep for the units a constant was compensating for whenever
  those units change.
- **A flat disc cannot draw a sphere, and the tell is that nothing is ever ENCLOSED.**
  `sphere_magic_shield` was four concentric sprites on `LAYER_VFX`, which draws in front of
  everything — so the character stood behind their own shield, and it read as a decal on the
  lens, the same failure the single-system weather effects had. A sphere has a FRONT AND A
  BACK with the body between them: `ShieldSphereFX` sorts every facet and every mote by the
  SIGN of its depth against the caster's live order, and that split is the only statement in
  the rig that anything is inside anything. It also must be rebased every frame — `YSortEntity`
  rewrites the caster's order when they walk, and a stale base pops the far hemisphere in front
  and flattens the sphere into a disc for as long as they are moving. Three quantities read off
  the same depth and have to agree or the motion reads as sliding: a mote SHRINKS, DIMS and
  SORTS BEHIND together. And the sphere is deliberately NOT squashed on Y — every other round
  thing here (ground pulses, telegraphs, puddles) lies on the FLOOR and is flattened because
  the camera looks at it at an angle; this one is in VIEW space, so flattening it makes it read
  as a disc lying under the character's feet.
- **A shield that cannot react to being hit is an aura.** `Health.ApplyDamage` opened with
  `if (IsDead || amount <= 0 || _invincible) return;` — a refused blow was SILENT, so no system
  downstream could tell a blow that was stopped from a blow that never happened, and the one
  moment the spell exists for produced no pixel at all. `OnDamageBlocked` is that seam. It fires
  only for a hit that was really turned away (not a zero hit, not one on a corpse), or a
  listener flashing on it flashes at nothing. The ripple it drives is placed by GEODESIC
  distance across the shell, so it wraps around the far side and converges at the antipode;
  straight-line screen distance would draw a disc that stops at the silhouette, which is a
  ripple on a plate rather than on a ball.
- **`SetInvincible` is one bool with three independent owners, so it must be SAVED AND
  RESTORED, never cleared.** The dev console's god mode, the F4 editor's test invulnerability
  and the shield all write it; the shield wrote `false` on expiry and switched off whichever
  of the other two was holding it. `SpellsRuntimeEditor` had already solved this the right way.
  Save/restore alone is not enough, though — the ORDER matters too: `SpellEffectRegistry.Track`
  is what evicts the previous shield, and eviction restores the flag that shield claimed, so
  tracking has to happen BEFORE the new controller claims it. Initialize first and the sequence
  runs backwards. Measured before the fix: cast twice and `IsInvincible` came back False with
  two spheres on screen.
- **The element chooses a flourish's palette; the spell's own swatch chooses its HUE.** 39 of
  the 74 shipped spells author a `particleColor` and `SpellCastFlourishFX` read none of them —
  a green laser gathered arcane violet, and a spell with no element (most of them) had no way
  to say what colour it was. `ElementPalette.RecolouredTo` moves the hue and keeps each field's
  own VALUE and ALPHA, because those are tuning: `hotCore` is near-white and `halo` is dim, and
  that spread is what makes a flourish read as a hot centre inside a soft bloom rather than as
  six sprites of one colour. Deriving the palette FROM the swatch instead fails hardest where
  it is least recoverable — `hostile_slash_dark` authors a 0.04 grey, and on an ADDITIVE
  material near-black adds nothing, so the flourish would not dim, it would disappear.
  Measured across the shipped set, the retint holds every field at value >= 0.85.
  `fireball` was the worst case: it is in NEITHER the `element` field NOR
  `MapSpellKeyToElement`'s legacy table, so it fell through to Arcane and gathered violet
  before throwing a fire ball. It now authors a red swatch. The alternative — `element: Fire`
  — was rejected twice over: the fire palette's core is ORANGE (1.00, 0.55, 0.10), and setting
  `element` also feeds `Health.MitigateDamage`, so it silently couples the spell to fire
  resistance. `meteor_shower` was the same case and took the same red. Ten spells are still in
  that state (boomerang, healing_aura, healing_totem, laser_beam_white, mine_basic, slash,
  smoke, summon_barbol, vortex_pull, vortex_push); the nineteen `anim_*` probes are not,
  because `AppliesTo` refuses them outright.
- **Two sentinels for "unauthored colour" is one too many, and the disagreement is visible.**
  `SlashExecutor` tested `particleColor != Color.clear` while `KiPalette.IsUnauthored` tests
  OPAQUE WHITE. No shipped spell has an alpha-zero swatch, so the executor's branch was
  unreachable and its `DefaultTint` was dead code — plain `slash` therefore swung a PURE WHITE
  arc, while the flourish read the very same field as unauthored and gathered arcane violet.
  Both halves were internally consistent and disagreed only on screen, in the half-second
  before the blade appeared, which is why it survived. `SlashExecutor.ResolveTint` is now the
  single answer to "what colour is this slash", applies the regular slash's brightness floor
  (a gather that ignored it would be darker than the blade it announces), and the flourish
  asks it through `ResolveSwatch` rather than reading the raw field. A spell type that grows
  its own resolved tint belongs in that switch, not inline at the call site.
- **Opaque white is NOT grey, even though it is achromatic.** The two rules meet in the
  flourish's colour resolution and their order matters: white is the "nobody authored this"
  sentinel and such a spell correctly keeps its ELEMENT's colour, while a real grey
  (`hostile_slash_gray`'s 0.59, `smoke_emitter`'s 0.78) is a deliberate request for the absence
  of colour and must desaturate the gather. Test the sentinel FIRST — checking saturation first
  catches white in the grey branch and reports eleven perfectly correct spells as broken.
- **An achromatic swatch has no hue, and `RGBToHSV` reports 0 for it — which is RED.** So
  blending a grey authored colour into a palette the naive way lights a grey spell with a pale
  PINK gather: measured on `hostile_slash_gray`, a 0.59 grey blade against a
  (1.00, 0.84, 0.84) core. Grey is a real request and what it asks for is the ABSENCE of
  colour, so `ElementPalette.Retint` short-circuits to a neutral at the field's own brightness
  below 0.02 saturation. The near-black guard is a separate case and both are needed:
  `hostile_slash_dark` is achromatic AND dark.
- **A swatch that reaches the flourish must be reachable in F4.** Making
  `SpellCastFlourishFX` read `particleColor` made the field LIVE for every type that shows a
  flourish, but `SpellFieldRelevance` exposed it for only 15 of 26 — so on Meteor, Aura,
  Totem, Summon, Mine, VortexField, ArcaneFlame, Smoke and SmokeEmitter the colour now drove
  the gather while the panel hid the control for it. The relevance test only fails the other
  way round (a field an EXECUTOR reads and the panel hides), so nothing caught it. All nine are
  exposed now; `WeaponLoadout` and `AnimationProbe` stay hidden, correctly, because `AppliesTo`
  refuses them. Note the meteors themselves were never violet — `MeteorMissileFX` hardcodes its
  own fire colours — so only the cast read wrong, which is exactly why it survived unnoticed.
- **A `Health` on a Building-layer object is unreachable code.** Every damage path finds its
  victims through a `LayerMask` — the player's melee targets NPC(9), a monster's targets
  Player(8), and `Projectile.ObstacleLayers` stops a shot on World(11)/Building(14) WITHOUT
  damaging it — and a blocking wall has to be on Building to block at all. So the ice wall
  shipped with `Health(100)`, a hit flash and a destruction sound that nothing in the project
  could ever trigger; it always died to its timer. `IDestructibleObstacle` +
  `DestructibleObstacleRegistry` are the seam, deliberately NOT a mask widening: melee on
  Building would query every painted collision cell in range on every swing, whereas the
  registry is normally EMPTY and costs a `Count` check. Projectiles reach it through their
  existing obstacle branch instead.
- **A particle module's axes must all be in the same curve MODE.** Assigning only
  `velocityOverLifetime.y` as a two-constant range leaves x and z as single constants, and
  Unity rejects the mismatch with `Particle Velocity curves must all be in the same mode` —
  once per frame, per system, for as long as the effect lives. Related, and the reason the
  velocity module is needed at all: a **Box shape emits along its own FORWARD**, which in a 2D
  scene is straight into the screen, so `startSpeed` on a box buys motion nobody can see.
- **Every `ElementalSprites` sprite is exactly 1x1 world unit, so a scale constant IS a world
  diameter.** `Sprite.Create` is handed the texture size as `pixelsPerUnit` for all eleven, so
  a 128 px Halo is no bigger in world than a 32 px HotCore — the resolution buys detail, not
  extent. `Ring`'s bright band peaks at normalized radius **0.78**, which makes the drawn
  boundary pinnable to the damage radius at ANY size: `ringScale = radius / 0.39`. Getting
  this wrong is invisible in code and brutal on screen — the arcane flame's only hard contour
  sat at 1.511 u against a 2.5 u damage circle, so 46 % of the area that hurt carried no
  readable pixel. Corollary: prefer an identity root and absolute per-child sizes over scaling
  the root, because a scaled root also scales any `Light2D` parented under it (that is what
  `WorldLightLoader`'s counter-scale by `1f / lossyScale` exists to undo) and silently renders
  an authored radius at several times its value.
- **Instantiating a shared prefab accepts EVERY component on it, including the one you are
  replacing.** `BoomerangExecutor` cloned the ball prefab from `ProjectilePrefabFactory` and
  never took its `Projectile` off, so an UNINITIALISED one rode along with its serialized
  defaults — and `Projectile.Update` expires on `range`, whose default is **20**. The boomerang
  was authored to turn at 26.25, so measured live the blade was deactivated and destroyed 20
  units out, 0.242 s into the throw: **the return leg had never run once in the spell's life**,
  and neither had the `lifetime = 3` timer waiting behind it. Its `FixedUpdate` also wrote
  `velocity = zero * speed` every step and its `Awake` set `freezeRotation = true`, both of which
  the boomerang only survived because its own component happened to be added later and therefore
  wrote last — there is no `DefaultExecutionOrder` anywhere in the project, so that was luck.
  Neither half was wrong alone; the COMPOSITION was, which is the same shape as the spawner
  coordinate drift and needs the same answer — a test that flies the whole throw.
  `enabled = false` as well as `Destroy`, because destruction is deferred to end of frame and a
  merely-destroyed component still runs its Update for the rest of it. Full findings:
  `.github/BOOMERANG_AUDIT.md`.
- **A damage radius is not a collision radius, and a curved path has to fit the room it is
  thrown into.** `BoomerangProjectile` swept walls with `hitRadius` — 0.75, authored generously
  so a near miss on a moving target still lands — where `Projectile` sweeps its own 0.15
  collider. Five times the width, so the blade caught on scenery nobody aimed at: measured over
  24 headings from one spot in the shipped world, **16 turned back early, one after 2.66 units
  of a 10-unit throw**. Two more angle-dependent faults sat behind it, and neither is in the
  geometry, which is rotation-invariant by construction: the loop always bowed the same way, so
  a wall on that side broke one heading while its opposite flew clean; and the bow leaves the
  aimed corridor by design, so scenery to the SIDE of a throw could stop it. The first answer to
  that last one was a clamp — measure the room and narrow the loop to fit — and it is the
  cautionary half of this note: it protected the flight and destroyed the spell, because from
  where the player actually stands in town **17 of 24 headings came back with less than half
  their bow, most under a tenth**, i.e. a boomerang flying straight. A curve is the spell's
  IDENTITY; shrinking it to avoid a bounce trades the thing away to protect the thing. What
  works is sizing each leg's bow off THAT LEG's length, so a leg cut short by a wall is a small
  lens instead of a full-width bulge on a three-unit run and the shape is the same at any size.
  Measured after: 24/24 headings identical in the open; from the player's own spot 0/24 flat,
  17/24 at full range, 24/24 caught, and the headings that still turn back early are exactly the
  ones with a wall inside the throw on a straight blade-thin cast — geometry, not a defect.
- **A rig whose owner SPINS cannot hang its trail off the owner's transform.** Everything in
  `ElementalProjectileVisual` that says "which way am I going" — the ghost trail at negative
  local X, the motion stretch on local X, the ember spray at `-transform.right` — was parented
  to a root the boomerang turns twice a second, so the trail orbited the blade instead of
  following it. They live under a non-spinning `Aura` child now, given a world rotation from the
  measured travel delta each frame; the accent (the blade itself) stays on the spinning root.
  The same file also had `LightningBoltFX`'s bug verbatim — `SortingConfig.Z_SKY (600)` passed
  as a `sortingOrder` on the **Entities** layer, which is below Decorations, WallsTop,
  ObjectsHigh, Projectiles and VFX, so every spell it drew rendered under the wall tops.
- **`AudioCatalog.asset` contains no `spell_*` id at all.** Every `PlaySfxById("spell_…")` in the
  project is a miss, and the only thing it produces is one warning per id, once. That is why
  `IceWallAudio`, `ShieldAudio` and `BoomerangAudio` synthesise their one-shots instead — the
  catalog path stays the better answer the day a recorded set is authored, and these become the
  fallback. Adding catalog entries with null clips fixes nothing: `PlaySfxById` warns on
  `entry.clip == null` exactly as it does on a missing id.
- **A spell's light is not a world fixture, so it must not be gated on the day/night cycle.**
  `ArcaneFlameController` subscribed to `DayNightCycle.OnLightsEnabledChanged` and did
  `SetActive(false)` on its whole light object during the daylight window — copying what
  `WorldLightLoader` does to every torch, which is right for a thing that exists all day and
  should only be seen to burn at night. It made the arcane flame the only one of the eleven
  spell controllers carrying a `Light2D` that went dark at noon, i.e. for most of a session,
  and the light is the half of that rig that says the ground is dangerous. Keeping it lit is
  free at noon: the BODY is on multiply and multiplying into an already-full ambient buffer
  changes little, while the additive CORE is exactly the half that should still read. It also
  retires a static-delegate lifetime (Domain Reload is OFF) that had to be unwound on all five
  exit paths.
- **On an additive stack a pulse that moves only SCALE is invisible.** A connecting tick grew
  the arcane flame's core by 42 % and its `Light2D` from 1.229 to 1.904 — and the summed
  additive alpha at the centre measured **2.274 before and after**, identical, because alpha is
  what that material adds. So the one moment the hazard exists for produced no change on the
  volume, and in daylight (where the light reads least) almost none at all. Alpha is COVERAGE
  there, which makes it the brightness dial: `PulseAlphaGain` 0.28 takes the sum to ~2.55, and
  the same arithmetic that caps `VortexFunnelFX`'s band count caps this — above ~3 the centre
  is flat white and a violet spell stops being distinguishable from a blue one.
- **`arcane_flame` was the fifth spell authored in Python pixels, and the last one aimed by a
  private constant.** `radius: 40` ÷ 16 in the executor, after `wallWidth`, the totem, the
  vortex and the puddle; the shipped definition authors `2.5` world units now and the divide
  is gone. It also carried `spawnAtMouse: 0` / `range: 0` while `ArcaneFlameExecutor` placed the
  zone at a hard `ThrowDistance = 2f`, so it was the only ground-placed spell in the project
  that could not be aimed — the two fields that say where a spell lands were inert on it.
  `SpellTargeting.ResolveGroundTarget` now owns FOUR executors (puddle, totem, vortex field,
  arcane flame), and `CastOriginContractTests` lists the helper rather than those executors,
  which is why removing one from `CasterEmissionCallsites` is the correct fix and re-inlining
  `ResolveCastStart` to satisfy the grep is not. `PuddleExecutor` still divides by 16 — it is
  the last one.
- **A spell's `element` and `particleColor` are load-bearing even when nothing looks broken.**
  `arcane_flame` shipped `element` BLANK, so its element came from `MapSpellKeyToElement`, the
  legacy switch whose own comment tells new spells not to grow it; and `particleColor` opaque
  white, which is the project's "nobody authored this" sentinel — the cast flourish therefore
  kept the Arcane palette by luck rather than by choice. Both are authored now (`Arcane`, and a
  violet swatch), and `ArcaneFlameSpellDataTests` pins them along with the world-unit radius,
  the aiming fields and the cooldown rule below.
- **A persistent ground hazard needs a cooldown longer than its own field, the same as a
  vortex.** `arcane_flame` ran 5 s on a cooldown of 2 with `maxInstances: 1`, so the player
  always had one out and could evict their own to reposition it — permanent area denial. Now
  cooldown 7 against duration 5, with `damagePerTick` 9 → 12 paying back the lost uptime.
- **A `LineRenderer` cannot draw a cone, and that is the third sighting of the same rule.**
  `flame_breath` drew its wedge as twelve points — origin, an arc, back to origin — which is a
  WIRE OUTLINE. A strip can bound a shape and can never fill one, so a breath weapon's entire
  silhouette was two thin strokes and a curve between them. `IceWallVisual` records it for a
  line and `VortexFunnelFX` for a column: the rig has to be shaped like the thing it draws.
  `FlameConeFX` is the filled answer — slices laid along the aim whose cross extent is the
  cone's real half-width at that distance, a white-hot throat over them, embers, a scorch and a
  light that reaches as far as the fire. Its slice count is a RESOLUTION dial, not a brightness
  one (`BODY_ALPHA_BUDGET` is divided by it), and `ORDER_CORE` / `ORDER_MUZZLE` are derived from
  `ORDER_BODY + SLICES` for the same reason the vortex's `ORDER_DUST` is.
- **A hand-derived `Quaternion.Euler` for a 2D aim is a coin flip, and this one lost.**
  `Euler(deg - 90, 90, 0)` is a MIRROR about the 45 degree diagonal. Measured over the eight
  facings: aiming east emitted north, aiming north emitted east, and 135 and 315 came out
  exactly REVERSED — six of eight directions sprayed the fire somewhere other than the damage,
  for the whole life of the spell. Only 45 and 225 were right, which is precisely why nobody
  caught it: a diagonal test passes. Live, aimed at `+X`, the mean of 35 particles sat at
  `(-0.04, +0.44)`. `Quaternion.LookRotation(dir, Vector3.forward)` is exact on all eight —
  world `+Z` is perpendicular to every 2D aim, so the up vector can never degenerate.
- **A sprite parent may only ever be turned about Z; an emitter parent usually may not be.**
  A sprite's quad lies in its own XY plane, so a `LookRotation` on it puts every sprite edge-on
  to the camera and they vanish — invisible rather than wrong-looking, the failure nobody
  reports. A `ParticleSystem`'s Cone shape emits along its own +Z and needs exactly that
  `LookRotation`. One transform cannot be both, so `FlameConeFX` carries TWO oriented children
  under one identity root, plus the usual unrotated ground-squash parent.
- **`coneLength` was the fifth Python-pixel sighting**, after `wallWidth`, the totem's radius,
  the vortex's radius and `range` on three executors. Authored 16.25 against an executor that
  divided by 16, so the breath reached **1.02 world units** on a camera 33.33 wide — three per
  cent of the screen, stopping short of the caster's own 1.86-unit sprite. The tell is always
  the same: the executor's fallback for an unauthored field (16.25 WORLD units) was sixteen
  times anything the asset could produce. `coneLength` is world units now and the asset says 5.5.
- **Reading `renderer.material` in a teardown ALLOCATES.** Measured: the material count rises
  by one on the read (5170 → 5171). `ConeBreathController` did it in both `CleanupAndDestroy`
  and `OnDestroy` — two clones per cast — inside the very method whose comment claimed the
  per-cast material had been removed. A teardown over shared materials has nothing to destroy
  and must not touch `.material` to find that out.
- **Four of a spell's six casting flags reach no gameplay code.** `allowMovement`,
  `interruptible`, `lockCastDirection` and `allowOverlap` have zero readers outside the F4
  editor and the inspector — grepped. Only `channelDuration` and `maxInstances` are live, and
  `maxInstances` only for effects that call `SpellEffectRegistry.Track`, which the cone did not.
  Same family as `animation_map.json` and the FSM's `Actions`/`Blackboard`: authored,
  round-tripped, and inert. Do not tune behaviour through them.
- **A speculative sound id must be gated on `HasSfx`.** `AudioManager.PlaySfxById` warns once
  per unresolved id BY DESIGN — an explicit id that fails to resolve is a data bug. The cone
  breath called it blind with `spell_flame_breath_loop`, which has never existed in the
  catalog, so the first cast of every session pushed a warning into a console this project
  requires to be clean. The interface documents `HasSfx` for exactly the "play a sound named
  after this spell, if one was ever authored" case.
- **Reapplying a status effect every damage tick is churn, never stacking.**
  `StatusEffectManager.Apply` REPLACES an effect of the same type, so the cone's burn — applied
  on all ten of its ticks per second per target — did a full remove-and-reapply and fired two
  events each time, tearing down and rebuilding the `SpriteTintStack` layer ten times a second
  for no extra damage. Refresh a DoT on its own timer, not on the damage clock.
- **The cast camera beat has ONE owner and it is `CameraFeelDirector`.** It already decides
  whether a cast is heavy from `prepareDuration`, `cooldownDuration` and `manaCost` against the
  profile, and fires `CastHeavy` as a RECOIL — away from the facing. A controller that fires its
  own doubles the shake and pushes it the other way. What the director cannot know is that a
  SUSTAINED effect connected, so that is the only beat a channelled spell should raise itself.
- **A cone tests its targets at their NEAREST POINT, not at their pivot.** An entity's transform
  sits at its feet, so a pivot test makes a large enemy standing squarely in the fire immune
  whenever its origin falls a degree outside the arc. `ConeBreathController.InsideCone` uses
  `Collider2D.ClosestPoint` and asks the RIG for the half-width at that distance
  (`FlameConeFX.HalfWidthAt`), so the wedge on screen and the wedge that hurts are one number.
- **A ki aura's palette is the wrong ramp for a FLAME, and the numbers say why.**
  `KiPalette` derives `Core` near-white on purpose — measured at **saturation 0.25** for the
  shipped orange, because a ki spine is meant to be almost colourless — and `Edge` at **value
  0.62**, dark. Running a fire cone's body from one to the other makes it washed out exactly
  where it is brightest and dim exactly where it has colour: the whole wedge summed to
  `(2.265, 1.367, 0.745)`, green at 60 % of red, which is cream. `FlameConeFX.FireHue` holds
  the VALUE at 1 across the whole cone and lets the alpha taper do all the fading — the old
  ramp darkened the tip twice, once through the colour and once through the alpha, and on an
  additive surface a dark colour adds nothing. The body never touches the white; the throat
  layer owns it. After: `(5.283, 1.779, 0.386)`, G/R 0.34, B/R 0.07.
- **On an additive sprite, the intensity dial is the COLOUR and it may exceed 1.** Measured,
  `SpriteRenderer.color` reads back an authored `2.400` unchanged, and both `Camera.allowHDR`
  and the URP asset's `supportsHDR` are on, so the excess survives to the framebuffer. That is
  the right dial because alpha is COVERAGE — the rule `WeaponSwapFlashFX` records — so
  reaching for the alpha budget to make fire fiercer WIDENS it into fog instead of hardening
  it. `FlameConeFX` overdrives the wedge (`BODY_GAIN` 2.65, `THROAT_GAIN` 2.9) and leaves the
  alpha budget alone, which is also what keeps the "slice count is not a brightness dial" test
  meaning what it says. The exception is particles: a particle's vertex colour is packed to
  `Color32` and CLAMPS, so the emitters take `FireHue` without the gain and get their intensity
  from saturation instead. Two ramps, one hue, and the split is documented at both ends.
- **Cooling a hue toward red is a statement about black bodies, so it is gated on a WARM
  swatch.** A flame goes orange to red because that is what hot matter does; a blue or violet
  breath has no such physics, and the same downward hue shift swings it through cyan.
  `FlameConeFX` applies `HUE_COOL` only when the authored hue is inside the warm band, and
  leaves every other swatch's hue exactly where the designer put it.
- **A spell whose name promises an EVENT has to contain the event.** `firework_launch` was
  three beats short of one: it flashed at the caster and threw an invisible projectile that
  expired and did nothing. There was no burst because there was no `impactPreset` and no
  code path to one, and the executor's own doc comment claimed an `ElementalImpactFX`
  starburst it had stopped producing whenever `AttachVisual` was rewritten. Every number in
  it was self-consistent — `damage: 0`, `range: 0`, `lifetime: 0` — and the only place the
  spell disagreed with itself was on screen, which is why it survived for the life of the
  project. `FireworkShellController` owns the whole timeline instead: climb, burst at apex,
  companions, report.
- **Riding `ProjectileExecutor` means inheriting `Projectile`'s defaults, and `range`
  defaults to 20.** The firework authored none, so its shell was deactivated 0.44 s into a
  flight at speed 45 — the same shape as the boomerang, which lost its entire return leg to
  that same 20. A cosmetic spell wants nothing from `Projectile`: no damage, no target
  layers, no sweep. Own the flight.
- **The report of a distant effect must arrive AFTER its picture.**
  `FireworkShellController.REPORT_DELAY_PER_UNIT` is 0.020 s per world unit travelled, so the
  default 6.5-unit shell is heard ~0.13 s (four frames) after it is seen. It costs one float
  and it is most of what makes a burst read as happening out there rather than on the lens.
  Its sibling is the WHISTLE, which is not decoration either: a rising sweep during the flight
  is what makes a player look up, so the burst lands on an eye already pointed at it.
- **`ctx.Direction` is already the cursor bearing, so a spell that ignores it is choosing to.**
  `PlayerController` resolves it every frame through `PlayerFacingResolver`, which reads the
  mouse via `MouseInputManager` — every spell cast by the player is handed the aim whether it
  uses it or not. The firework took `direction.x` alone, as a 35 % lateral nudge on a flight
  that was always straight up, so aiming moved the burst by a couple of units and could never
  move it down or behind the caster. It flies the full distance along the bearing now, with a
  BOW above the straight line scaled by how horizontal the aim is (`ARC_BOW_FRACTION`) — a
  vertical shot has no straight line to bow away from, and without the bow a firework aimed
  across the street is a bullet. The two curves are separate on purpose: progress along the
  line decelerates, the bow is symmetric in raw time, and driving the bow off the eased value
  skews its peak to the end and reads as a hook.
- **A burst metres above the ground cannot light the world with its own `Light2D`.** A point
  light has a radius; a detonation overhead has to reach the tilemap, the buildings and every
  entity at once, which means the GLOBAL light. `WeatherGrade` reached that conclusion first
  and hardcoded its hook to lightning; `SkyFlash` is the same mechanism with the weather taken
  out, composed into `DayNightCycle.UpdateLighting` beside the strike and ADDING to it. It is
  ticked from `Update` and never from `UpdateLighting`, which property setters also call — an
  envelope advanced there runs several times in a frame whenever anything scrubs the clock.
- **Opaque white is the "nobody authored this" sentinel, and for exactly one spell it is the
  right answer.** `FireworkPalette.From` routes it (through `KiPalette.IsUnauthored`, so the
  two cannot drift) to the five-colour festival assortment, because a firework with one hue is
  a flare. Every other swatch gives a single-hue shell whose stars spread ±0.075 turns around
  the authored colour — wide enough that a red shell throws orange and magenta, narrow enough
  that it is still a red shell. The achromatic guard is separate and mandatory: `RGBToHSV`
  reports hue 0 for grey, and hue 0 is red.
- **A `[SerializeField]` on a component that is `AddComponent`-ed has no way to be filled.**
  `ChatSystem._catalog` sat null for the life of the project because `GameplaySceneSetup`
  builds the ChatSystem on a bare GameObject and no scene contains one — so every persona
  lookup returned null, no NPC greeted, and `GenerateReply` returned on its first line.
  Nothing failed: the field was legal, the inspector showed a slot, and the slot belonged to
  an asset nobody ever opened. Any system created that way needs a `Resources.Load` from a
  SUBFOLDER, a `ServiceLocator` entry, or an explicit setter called from bootstrap.
  `AssetConventionsTests` whitelists each such `Resources/` folder against its call site, so
  the justification is written down where the exception lives.
- **A chat system with no chat-capable entity is a full test suite over nothing.** 225 green
  tests covered `ChatSystem`, and every one built its own catalogue, its own
  `NPCInteractable` and its own player. In the shipped game `EntityRegistry.RegisterNPC` had
  ZERO callers, nothing added `NPCInteractable` to a spawned entity (grep the script GUID
  across `*.prefab`/`*.unity`/`*.asset`: zero hits), and `VendorNPC` was never instantiated —
  so `TryOpenChat` could only ever answer "no hay nadie cerca". Same shape as
  `SPAWNER_COORDINATE_SPACE_DRIFT`: assert on the composition and on the shipped bytes, which
  is what `ShippedChatDataTests` now does.
- **`NPCInteractable.Interact()` means "open this vendor's shop", not "talk to this person".**
  It had no caller at all until `ChatSystem.TryOpenTradeWithTarget` — reached from the chat
  panel's Trade button — so `VendorShopUI.OpenShop` was authored, wired and unreachable. A
  vendor is a character first: the way to the counter is through a conversation, not a second
  key. Don't route a general interaction layer through that event.
- **`<Keyboard>/e` is no longer double-bound, and `p` was too.** `e` was on `Interact` AND
  `SpellSlash`; `p` was on `Pause` AND `SpellMeteorShower`, so pausing threw meteors. Slash is
  now `z`, meteor shower is `o`, and both moves need the legacy `KeyCode` fallback in
  `InputService.EnumerateSpellBindings` changed to match — the binding alone is half the
  answer. `VendorShopUI` also stopped closing on `e`, because Unity's Update order between it
  and the interact reader is undefined and one press would have closed the shop and re-opened
  the chat behind it, or not, depending on the frame.
- **`EntityStats.chatRange` is live now.** It was authored on every shipped entity (vendors 2,
  hostiles 0) and read by nothing; `EntitySetup.ConfigureChat` consults it as the fallback
  behind `NPCPersonaDefinition.chatRange`. The two agree by construction because both were
  imported from the same Python `assignments.json`.
- **A control with no prompt is a control that does not exist.** Pressing E near an NPC opened
  a conversation for as long as `PlayerInteractionController` existed — but chat was a FALLBACK
  outside `InteractableRegistry`, so an NPC never produced an `InteractionPromptInfo` and
  nothing on screen said the key would work. The player learns "E chops trees" from the badge
  over a tree and has no reason to try it on a person. `NPCConversationInteractable` puts them
  in the registry; the badge reads `Conversar` over the character's name, plus `· comercia`
  for a vendor, which is otherwise only discoverable by holding a conversation first.
- **Wrapping an NPC as an interactable is only safe if it does NOT go back through
  `TryOpenChat`.** That method runs its own proximity sweep over every persona, so the registry
  would pick one character and the sweep another — the badge naming somebody the key does not
  talk to. `BeginInteraction` calls `ChatSystem.OpenChat(gameObject)` with the target already
  in hand, so there is exactly ONE search. The controller's own fallback stays for hand-placed
  entities resolved through the by-name catalogue.
- **A character who WALKS must use `InteractableRegistry.RegisterDynamic`.** A plain `Register`
  is indexed by the position held when the spatial hash was last rebuilt, and it rebuilds only
  on membership change — so a strolling vendor goes on being looked up where she used to stand.
  It fails only above the hash threshold of 24, i.e. it passes in an empty test scene and
  breaks in the shipped world, measured at 94 registered entries (88 harvest nodes + 6 NPCs).
- **A conversation is not a leashed work session.** `PlayerInteractionController` assigns
  `_session` on every accepted press and cancels it when the player drifts 0.35 units, so an
  NPC reporting `IsInteracting` would end the conversation on the first half-step. Returning
  false makes the controller drop the reference the next frame, which is correct — from there
  `ChatSystem` owns it and Escape or Enter close it. `CancelInteraction` must therefore be a
  NO-OP: chat engages `InputBlocker`, the controller suppresses on the next frame and tears the
  session down, so closing the chat there would close it one frame after it opened.
- **`InteractionBounds` for a character is the FOOTPRINT, never the sprite.** A villager is
  drawn upward from their feet — Gatita is 2.4 units tall — so measuring against the sprite
  raises the badge for a player standing on a roof two units above her head.
  `EntityColliderConfigurator.GetBodyCollider` is the same box the physics uses; `HarvestNode`
  records the identical rule for a tree canopy.
- **A `LayoutElement` that sets only `preferredHeight` does NOT stop its row expanding.**
  uGUI resolves each layout property INDEPENDENTLY, taking it from the highest-priority
  component that supplies one — so a `LayoutElement` (priority 1) wins the preferred height
  while leaving `flexibleHeight` at its unset -1, and the value actually used is the
  `HorizontalLayoutGroup`'s on the same GameObject, which reports **1** whenever
  `childForceExpandHeight` is on. The chat input row was therefore competing with the
  conversation for every spare pixel: measured at a 340-tall panel, 80 px of text box against
  a 32 px preference, and enough overflow at the DEFAULT size to clip the last message line
  mid-word. Any row carrying both components needs an explicit `flexibleHeight = 0`.
- **A resize grip must not be ROTATED into its corner.** The grip is pivoted on the corner it
  occupies, so a `localRotation` turns the rect about that pivot and swings the whole square
  outside the panel — measured, a top-right grip landed at x=[540..556] against a panel whose
  right edge is 540, i.e. entirely outside the window it resizes. A negative `localScale`
  moves it the same way and flips the triangle's winding as well. `TriangleHandleGraphic`
  mirrors its own MESH instead, and shares `ResizeGripCorner` with `PanelResizeHandle` so the
  glyph and the drag it advertises can never name different corners.
- **A grip's corner is fixed by the panel's PIVOT, not by taste.** A panel grows away from its
  pivot and never towards it, so a bottom-left-pivoted panel — which is what anything pinned
  near the bottom of the screen must be — has its bottom edge nailed down and a bottom-right
  grip could only ever change its width. `PanelResizeHandle.Corner` selects the sign of the
  vertical delta; `BottomRight` is the default because all four resizable runtime editors
  (F1/F4/F7/F8) are top-left-pivoted and none of them passes one.
- **uGUI does not lay out in EditMode**, so a test that measures `rect.height` after building
  a panel through reflection is measuring the RectTransform's default 100 px and asserting on
  nothing — `LayoutRebuilder.ForceRebuildLayoutImmediate` does not rescue it, because the
  layout groups never got their enable-and-dirty cycle. Sum the authored `LayoutElement`
  values instead. Same family as the Awake/OnDestroy trap above.
- **PlayerPrefs is MACHINE state, not fixture state.** A test asserting a UI default must
  delete the keys in `SetUp` AND `TearDown`: they survive the run, the Editor and the reboot,
  so resizing a window once by hand would leave the default-size assertion failing forever,
  on that machine only, for a reason nothing in the test name mentions.
- **The editor workspace layer cannot persist non-editor UI.** Every entry point on
  `IEditorWorkspaceService` is typed on `GameEditorManager.IGameEditor` and keyed on
  `EditorName`, so a gameplay panel would have to impersonate an editor — and would then hang
  its geometry off editor open/close hooks it never passes through. `EditorPanelState` and
  `DraggablePanel.CaptureState/ApplyState` ARE reusable (Core and UIKit, no editor
  dependency); the service is not. The precedent for a resizable gameplay panel is
  `MusicPlayerHUD`: PlayerPrefs floats under `valkur.<widget>.<field>`, written on drag END
  rather than per frame, since that is a file write.

## Player character pipeline (2 directions)

`dwarf`, `barbarian` and `elven` are built from **side-view art drawn in ONE direction**,
and mirrored. Which direction is a per-sheet fact to be measured, not assumed — wave4 faces
west, wave5 faces east. `mague` and `valkyrie` still run on the legacy 8-direction strips. The two pipelines coexist on purpose and have different owners:

| | wave3 (dwarf, barbarian, elven) | legacy (mague, valkyrie) |
|---|---|---|
| Source | `staging/players/<char>/` (gitignored, repo root); elven is `elf_wave4/` **plus `elf_wave5/`**, dwarf is `knight_wave4/` **plus `knight_wave4_armed/`** | `Art/Characters/<key>/<key>_<state>.png` |
| Cutter | `tools/atlas/wave3/build_player_frames.py` | — |
| Binder | `PlayerFramesImporter` (`Valkur > Players > Import Frame Sheets`) | `PlayerCharacterAssetBinder` (`Valkur > Setup > Rebuild Player Character Assets`) |
| On disk | one tightly-cropped PNG per frame under `Art/Characters/<key>/<state>/`, named `<key>_<state>_<e\|w><i>.png` | one 5120x128 strip per state at `Art/Characters/<key>/`, 128 px cells |
| Record | `tools/atlas/generated/player_frames_manifest_wave3.json` | — |

```text
slice_prop_sheet.py --all --sheet-dir staging/players/<char> --out <slices>
#   elf_wave5 also needs --config tools/atlas/wave5/elf_wave5.slices.json: one sheet
#   draws the loosed ARROW as its own object, another the summoned bow detached from
#   the hand conjuring it, and neither is a frame
wave3/build_player_frames.py <slices>     # align, scale, mirror, write manifest
Valkur > Players > Import Frame Sheets (Dry Run) then (Apply)
```

Every staging folder feeds ONE slices directory — the builder walks all three players in a
single pass and rewrites the whole manifest, so slicing only the new wave silently drops the
other two. **Unless you pass `--only <player>`**, which builds just those and MERGES the
result into the manifest on disk, leaving the other players' records untouched. Reach for it
whenever another character's staging is mid-wave: a full run reissues every player from
whatever happens to be staged at that moment, which is how an unrelated character gets
resliced without its `--config` and quietly reshipped.

- **The mirrored half is baked as its own sprite.** `DirectionalAnimator` never flips —
  `ChaseState` says so — so the importer fills all eight buckets from two. Each state's list
  is `framesPerDirection * 8` and repeats each sprite four or five times. `knight_red`
  already shipped this way.
- **Facing is per SHEET, not per wave, and `elf_wave5` proves why.** The elf's archer and
  bard sheets are drawn facing RIGHT while every wave4 sheet of the same character faces
  LEFT, so `build_player_frames.py` keys facing off `EAST_FACING_SHEETS` rather than one
  global constant. Measure it off the HEAD — the pointy ear points BACKWARD and the face
  points forward — never off the silhouette or the weapon, and never off a whole-body
  correlation against a known sheet (tried: the margin between a pose and its mirror is
  under 0.05 NCC across differing poses, which is noise).
- **Which half is the mirror has to be MEASURED, and the wave4 art faces WEST.** All three
  characters are drawn facing left, so the authored frames are the `_w` half and the `_e`
  half is their mirror; S/SE/E/NE/N take `_e`, NW/W/SW take `_w`. Getting it backwards is
  invisible everywhere except in play: `Direction.East` is +X (`DirectionalAnimator.FrameLogic`
  resolves 0° to East), so putting west-facing art in the east buckets makes every character
  face AWAY from the cursor — while each individual frame, every contact sheet and every
  count in the manifest still looks right, because the mapping is internally consistent and
  disagrees only with the art. It shipped that way once. `wave2/build_knight_frames.py` had
  already recorded the same fact for `knight_red` ("The art faces left"), which is the note
  that should have been read before assuming. `PlayerTwoDirectionRigTests` now pins the
  bucket/suffix contract and that the two halves really are mirrors of each other; it cannot
  pin which way the art points, so re-measure that by eye when a new wave is staged.
- **A sheet that opens mid-pose declares a `REFERENCE_FRAME`, not a `SCALE_OVERRIDE`.**
  Both wave5 unarmed casts break the frame-0 assumption in opposite directions, and both
  are visible next to the idle: `elf_spellcasting_4` opens with the casting arm thrown
  straight up, and `body_box` measures to the top of the raised HAND (478 px against the
  344-346 px plateau frames 3,4,5,7 agree on), so normalising on it rendered the elf a head
  short; `elf_spellcasting_5` opens in a deep crouch (309 px against the 397-403 px plateau
  of frames 4,5,6) and rendered him oversized. Pointing the reference at a frame on the
  plateau keeps the number a MEASUREMENT. Reach for the multiplier only when no frame of
  the sheet is neutral. A head-correlation calibrator was written for this and deleted:
  it returned 1.20x for `elf_punch`, which is shipped correct with no override, so it
  failed its own control.
- **`own_object_only`'s cell test has to be 2D.** On a 4x2 sheet the archer's bow is taller
  than the gap between rows, so the bow drawn in the row ABOVE lands inside this frame's box
  in the SAME COLUMN — an x-only ownership test waved it through as a brown arc floating over
  the archer's head in two frames of eight. Nothing in waves 3-4 is tall enough to cross a
  row, so it took a bow to find it; adding the row test changed ZERO of the 538 already-shipped
  player sprites, which is how it was verified.
- **`build_player_frames.py` scales each state off FRAME 0's foot-to-crown height** (unless
  `REFERENCE_FRAME` says otherwise). Every
  sheet in the wave opens on a neutral standing pose, and that is the only frame whose height
  means "how big is this character" — the AI rendered each sheet at its own zoom. Both
  obvious alternatives fail, in opposite directions and measurably: the tallest bounding box
  is weapon-inclusive (an axe raised overhead shares a connected component with the hands
  holding it), which rendered the barbarian's overhead swing at 59 px against a 115 px idle;
  the median is dominated by whatever the sheet mostly does, so on a death — four of seven
  frames prone — it took a LYING body as the standing reference and rendered the knight at
  405x263. A sheet that opens mid-pose needs a `SCALE_OVERRIDE` entry; `elf_attack_jump_8f`
  is the only one, at 0.871. All 26 shipped states land within 2.6% of their own idle.
- **The ground line is the lowest row with real horizontal EXTENT, not the lowest pixel.**
  Same reason: a blade sweeping the floor, a cape tip and an outstretched leg are slivers,
  boots are not. Anchoring on the lowest pixel floated the character for the rest of the
  swing. Nothing is reserved below that line, so the canvas bottom IS the ground line and the
  postprocessor's `(0.5, 0)` pivot lands on the feet.
- **`TARGET_BODY_PX` is 115** because that is what the five legacy characters measure. Every
  melee range, projectile offset and camera lead tuned against the old art still reads.
- **A wave OWNS the whole character.** `PlayerFramesImporter.ClearUnlistedStates` empties any
  state the manifest does not name — unlike `MonsterFramesImporter`, which leaves unnamed
  slots alone. A monster manifest is often a partial refresh of a hand-authored asset; a
  player wave is a replacement, and the barbarian's unnamed `cast`/`damage`/`death` were still
  holding the previous 8-direction art of a different-looking character. `EntityAnimationBinder`
  falls an empty slot back to a neighbour, so the player sees the right character in a less
  specific pose instead of the wrong character.
- **A player never used to pick a variant, so every alternative animation was dead data.**
  Only `FSMMonsterBrain` set one, through the monster FSM's `AttackState`; the two-argument
  `SetState` reuses the active index, which on a player was `-1` forever.
  `PlayerController.NextVariant` now rotates one per action, so the elven character's three
  punches and three spellcasts, the dwarf's four unarmed attacks and the barbarian's two axe
  swings all render. Rotating, not randomising: a random pick repeats the same swing back to
  back about one time in N and reads as the animation having failed to change.
- **A second look for the same character is a LOADOUT, not a second character.**
  `EntityAssetConfig.loadouts` is a list of named override sets; each names only the states it
  has art for and every other state keeps the base art. The dwarf ships `armed` (idle, walk,
  chase, attack) from `staging/players/knight_wave4_armed/` and will never have an armed hurt,
  death, recover or spellcast — nobody is going to redraw six more sheets so the character can
  be hit while holding a sword. A second `EntityAssetConfig` would have to duplicate those six
  (two copies drifting apart on the next import) or leave them empty and fall back to a
  neighbour, which puts the character in the wrong POSE rather than merely the wrong hands.
  The swap is `PlayerLoadoutController` calling `EntityAnimationBinder.ApplyLoadout`, which is
  the SAME bind path as boot — the fallback chain (walk falls back to idle, chase to walk,
  attack to cast) decides what an artless state shows, and a second implementation of it would
  answer differently. `AnimState` is untouched: there is no "armed idle", there is idle drawn
  with a sword, so every locomotion whitelist and revert path keeps working because none of
  them can tell a swap happened. Toggled in game by the `weapon_toggle` spell
  (`SpellType.WeaponLoadout`, **B**), whose `loadoutKey` names the loadout; an unknown key is
  refused and logged rather than read as "unequip".
- **A swap that replaces four sprite sets in one frame needs something over the top.**
  Without it the character POPS from one set of hands to the other with nothing to read as a
  cause. `WeaponSwapFlashFX` covers the cut: an additive bloom over the silhouette, a halo, a
  band that SWEEPS along the body (up when drawing, down when stowing — the only piece that
  knows the direction), an expanding ring and twinkling motes, plus the body's own colour
  driven through `SpriteTintStack` on `TintLayer.Equip`. It is additive on purpose: on the
  alpha material the brightest pixel a glow can make is its own colour, so a flash meant to
  wash the body out cannot blow out. It FOLLOWS its owner rather than being parented, because
  the toggle allows movement and parenting would inherit the entity scale — and scale a
  `Light2D` radius with it. Adding `TintLayer.Equip` also walked straight into
  `SpriteTintStack`'s hand-maintained `LAYER_COUNT = 9`, whose failure mode is an
  `IndexOutOfRange` on the first `Set` of the NEW layer, i.e. inside the new effect rather
  than in the stack; it is now derived from the enum.
- **On an additive material, alpha is COVERAGE and colour is brightness — so a "dark" effect
  is authored by darkening the colour and leaving the alpha alone.** Every layer of
  `WeaponSwapFlashFX` is on `ElementalSprites.SharedAdditiveMaterial` (`SrcAlpha/One`), which
  adds what it is given: a deep indigo at alpha 0.85 still covers the whole silhouette, it
  just adds dark violet light instead of white. Reaching for the alphas instead makes the
  flare FAINT, not dark, and a faint flare stops hiding the cut it exists for. The dwarf's
  stow therefore runs `Tint (0.20,0.09,0.34)` / `Hot (0.34,0.15,0.52)` at the SAME alphas as
  the pale draw. The one layer that could genuinely darken the character is `Body`, which
  goes through `SpriteTintStack` and MULTIPLIES — it is deliberately held near white in both
  directions (measured live: `(0.90,0.87,0.95)` at the punch's peak), because dragging the
  body down reads as the dwarf being dimmed rather than as a dark spell going off around him.
- **A flare hides a cut, so it fires WHERE the cut is — and the two halves of a toggle put
  the cut at opposite ends of the same animation.** Drawing has to swap the art on the cast
  frame, because the draw animation is showing a weapon the character must already be
  holding. Stowing cannot: the sheathe is that same sheet run backwards and shows the weapon
  in hand for all eight of its frames, so swapping on the cast frame plays 1.2 s of putting
  away a sword that is no longer there. `PlayerLoadoutController.ToggleLoadout` therefore
  COMMITS a stow immediately and defers only its ART — measured live on the dwarf, the flare
  spawns at 1.16 s while `ActiveLoadoutKey` is still `armed` and the swap lands at 1.20 s.
  Three separate things make that work and each fails differently. The commit has to be
  immediate even though the art is late, because `ShouldPlayCastReversed` reads
  `SwappedThisFrame`/`LastSwapStowed` in the SAME frame the executor ran and a late answer
  leaves the sheathe playing forwards, i.e. drawing the weapon twice. The delay is not a
  constant: `TriggerCastAnimation` hands over the cast window it just measured
  (`ScheduleStow`), which is the only place the sheathe's real length exists, since it
  depends on the resolved variant and that variant's own speed multiplier — the 0.35 s
  `STOW_FALLBACK_DELAY` is a backstop against hanging armed forever, not the normal path,
  and a stow that silently took it would land four times too early. And the flare leads the
  swap by `FLASH_LEAD = 0.04f` because `WeaponSwapFlashFX`'s bloom peaks 12 % into its 0.34 s
  cycle: firing them together lands the cut while the flash is still ramping, which is the
  one frame the whole effect exists to cover. `SetLoadout` stays immediate and flare-free in
  both directions — it is what the animation probes use to park a character in a loadout, and
  it CANCELS any pending stow, or one armed a second earlier would undress the character the
  probe just dressed.
- **Every authored animation has a spell that plays it, and half of them could not.**
  `SpellType.AnimationProbe` is an inert spell — its executor is deliberately empty — that
  exists so an animation can be selected and watched in the Spells Editor. It was needed
  because most animation states are unreachable from casting: idle/walk/chase belong to
  locomotion, damage to the hit flow, death and recover to `DeathSequenceController`. The
  dwarf ships one `anim_<state>` probe per sprite folder, `audience = None` so they sit in the
  picker's "unassigned" tab rather than claiming to be player content, and
  `AnimationProbeSpellTests` keys its coverage check off the FOLDERS on disk — which is what a
  wave actually produces — so a new animation without a probe is a red test rather than
  something nobody notices. Two things had to be fixed for the preview to mean anything:
  `SpellDefinition.previewAnimState` (a string, because `AnimState` lives in `Valkur.Gameplay`
  and `Valkur.Data` may not reference it — the constraint `LoadoutStateSheets.state` answers
  the same way), and `DirectionalAnimator.CopyVariantsFrom`, because the preview rig
  hand-copies the seven base sets and that copy is lossy in exactly the way that matters: with
  no variants installed, `VariantForSpell` answered -1 for everything and EVERY spell previewed
  the base cast pose. The `anim_armed_*` three are the honest exception — a loadout's
  locomotion only exists while the loadout is worn, so they mirror whatever the live player is
  wearing.
- **A spell's animation state is DATA and it must reach the GAME, not only the preview.**
  `SpellDefinition.animState` names one of the eight states; empty falls back to Attack for an
  attack-routed spell and Cast otherwise. It shipped first as `previewAnimState`, read only by
  the Spells Editor's preview panel — and that was the bug: LEFT CLICK with the editor open
  does not drive the preview, it casts for real through `PollRedirectedPrimaryCast`, and
  `TriggerCastAnimation` resolved the state from `usesAttackAnimation` alone. So nine of the
  nineteen probes fell through to Cast, reserved no cast variant, and took whatever
  `NextVariant` handed them: selecting "Anim: Die" cast a rotating spellcast. Entering an
  arbitrary state needs TWO more things, and each is a separate way to break it — the revert
  must hand control back from whatever state was entered (`_castAnimState`; the old whitelist
  covered three, so a probe asking for `death` would have held the corpse pose forever), and
  the locomotion override must hold off while a cast window is open, or a spell naming
  Idle/Walk/Chase is overwritten on the very next frame and never renders. Normal casts are
  untouched by both: they enter Cast or Attack, which locomotion never overrode anyway.
- **Which animation state a spell plays is DATA, not a hard-coded key.**
  `SpellDefinition.usesAttackAnimation` routes a spell through `AnimState.Attack` instead of
  `AnimState.Cast` — a swing rather than a conjuring. It used to be a literal comparison
  against `slash_regular`, true of exactly one spell, and the cost was invisible: on the dwarf
  it made `punch` and `kick` UNREACHABLE. Nothing but the regular slash ever entered Attack,
  and the regular slash is reserved for `armed_slash`, so `NextVariant(Attack)` was never
  called and two authored animations rendered no frame anywhere in the game. Reservations for
  an attack-routed spell are looked up among the `attackVariants`, so which animation it plays
  is still pinned on the CHARACTER. Shipped today: `slash_regular` → `armed_slash`,
  `vortex_push` → `punch`, `vortex_pull` → `kick`.
- **A move that is the undo of another one plays the same sheet BACKWARDS.** The dwarf's
  sheathe is his draw reversed — one motion, one sheet, read either way — so
  `SetState(state, dir, variant, reversed)` maps the cursor through `FrameAt` instead of
  counting down, and the loop, the `holdLastFrame` branch and the frame clock are inherited
  unchanged. Two things make it work and both are easy to miss: a changed PLAYBACK DIRECTION
  counts as a state change (drawing then stowing is Cast-to-Cast on the same variant and the
  same facing, so without it the early-return swallows the sheathe and it replays the draw),
  and `RefreshCurrentFrame` has to map too or turning mid-sheathe snaps to the mirror-image
  frame. Who decides is `PlayerLoadoutController.LastSwapStowed`, not the spell: `weapon_toggle`
  is the same spell in both directions and cannot tell you which way it went. The window is
  one frame (`SwappedThisFrame`), which is exactly the gap between the executor running inside
  `TryCastByKey` and `TriggerCastAnimation` running right after it.
- **An action can be shorter than the art drawn for it, so a variant carries its own pacing.**
  `animationSpeedMultiplier` and `holdLastFrame` on `AttackVariant`/`CastVariant` are a SECOND
  multiplier beside the entity's: the entity's says how fast this creature moves and is tuned
  once per monster, the variant's says how long this animation may take. The dash forced it —
  in real gameplay `DashExecutor` teleports the body with a single `rb.MovePosition` and its
  streak and ground wake last 0.14 s (`moveDuration` only drives the FX and the Spells Editor
  preview, whose synthetic caster has no `Rigidbody2D`), against eight `charging_sprint`
  frames that read for 1.2 s at the normal 0.15 s each. The dash's `charge` variant therefore
  ships at **4x with `holdLastFrame`**: the lunge runs in 0.30 s, inside the 0.35 s cast
  window, and the landing pose holds the remainder instead of the lunge starting over. Both
  halves are load-bearing — `GetStateLength` multiplies by the variant's speed, so a window
  sized from it would otherwise hold the pose four times longer than the animation runs, and
  Cast/Attack LOOP by default (only Death played once), so a move that ends somewhere restarts
  and reads as a stutter.
- **A spell can RESERVE a cast variant, and a reserved variant leaves the rotation.**
  `CastVariant.spellKeys` names the spells that always play that animation; the binder
  installs the reservation table in the same `SetVariants` call as the sprite sets, because
  it DROPS variants that resolved to no frames and an index computed from the authored list
  would slide off from the first empty slot on. `PlayerController.ResolveCastVariant` asks
  `VariantForSpell` first and only falls back to `NextVariant`, which now skips reserved
  indices — both halves are needed and they are different statements: the claim is what makes
  the pose always play for that spell, and leaving the pool is what stops the other four
  spells borrowing a pose drawn for one. The reservation lives on the CHARACTER, not on the
  `SpellDefinition`: `spell_3` is a different animation on the dwarf than on the elven, so a
  spell naming an index would be asserting something about art it has never seen. Shipped
  today, all on the dwarf: `fireball` → `spell_3`, `healing_aura` → `spell_2`,
  `weapon_toggle` → `armed_equip` (the draw), `dash` → `charge` (the shoulder-first lunge),
  and every slash → `armed_slash`, so a slash is always swung with the weapon whether or not
  the armed loadout is worn. `slash_regular` is reserved on the ATTACK variant of that name
  instead of the cast one, because it is the single slash that routes through
  `AnimState.Attack` — `AttackVariant` carries the same `spellKeys` field for exactly that
  one case, and without it "every slash draws the weapon" would be true of four out of five.
  `PlayerFramesImporter.ApplyCastVariants` rebuilds the variant list on every import and
  carries the reservations across BY KEY — by position would move them onto the neighbour the
  first time a wave adds a sixth spellcast. A variant declaration in `build_player_frames.py`
  may carry a third element, the spells it is reserved for; that is a CREATION DEFAULT and an
  authored value always wins, the same shape `TilesetRulesetImporter` uses for terrain names.
  Both halves earn their keep: a new animation that ships unpinned is an unreachable rotation
  step until someone remembers the second step, and a re-import that overwrote the authored
  value would undo every pin a designer has moved.
- **A cast's animation window was a constant, so most of the animation never rendered.**
  `TriggerCastAnimation` held Cast for a flat 0.35 s against a `frameInterval` of 0.15 s — an
  eight-frame spellcast was cut at frame three, every time. It now takes the larger of that
  historical floor and `GetStateLength(state, variant)`, measured AFTER `SetState` has turned
  the animator, since `GetStateLength` reports the CURRENT direction's frame count. Related,
  and the reason the beam looked broken: a channelled spell re-enters that method EVERY FRAME
  while held, and advancing the rotation there handed `SetState` a different variant sixty
  times a second — a changed variant counts as a state change, so the pose restarted at frame
  0 on every one of them. The variant is reused for as long as the same cast's window is open.
- **Variants are per STATE, not per attack.** `DirectionalAnimator._variantsByState` is
  indexed by `AnimState` because elven ships three casting animations, and a second parallel
  cast-only array would have paid the positional tax `AttackVariant`'s own doc-comment exists
  to complain about. `SetAttackVariants` is a thin wrapper over `SetVariants(Attack, …)`, so
  every monster caller is untouched. On the data side the two lists stay separate classes:
  `CastVariant` carries no damage/range/cooldown, because a spell's damage is on its
  `SpellDefinition`, and a shared base would need `[SerializeReference]` and change how every
  already-authored attack variant round-trips.
- **`AnimState.Recover` is the eighth state, and the only one entered by the death flow.**
  `DeathSequenceController.ReviveRoutine` plays it after the body is solid and the corpse is
  despawned, for exactly `GetStateLength(Recover)` — measured, not a constant, and skipped
  entirely on `ForceRevive` (the DevConsole cheat, where waiting out an animation is the
  opposite of what was asked). It is in `TickCastAnimRevert`'s revert whitelist as well as
  being owned by that coroutine, because CLAUDE.md's own warning applies to it: a state
  locomotion refuses to override and nothing reverts is a soft lock, and a coroutine can be
  killed by a scene change mid-rise. A character with no recover art falls back to idle in
  `GetSpriteSet`, and `ResolveRecoverDuration` returns 0 for it so the revive does not pause
  on a still pose.
- **Extra attacks are `attackVariants`, never new `AnimState` values** — the reason is in the
  gotchas below. The importer refreshes a variant's `sheets` and leaves its damage/range/
  cooldown/weight exactly as authored.
- **`staging/` lives at the repo root, not under `Assets/`.** Unity imports everything under
  `Assets/` whether or not it is referenced; these are ~250 MB of source PNGs that only the
  Python pipelines read. `AssetConventionsTests` enforces the boundary
  (`HardRules_AssetsRoot_OnlyContainsWhitelistedEntries`, and `HardRules_NoIterationSuffixes`
  against the `_vN` variant names staged there).
- **`elf_wave5` ships its ACTIONS and stages its LOCOMOTION, and that split is structural.**
  `EntityAnimationBinder` builds variant lists for exactly two states — Attack and Cast — and
  `PlayerController.NextVariant` rotates one per action, so an idle/walk/chase variant has no
  selector and would never render a frame. The archer's and bard's seven locomotion sheets
  therefore stay in `stagedNotShipped` until there is a loadout system; their attack and the
  four loadout casts ship as variants. Shipping the bow as an attack variant means it appears
  in the elf's empty hands every fourth swing and vanishes again — the exact pop the barbarian
  entry avoids — and it was taken deliberately, which is why `bow` sits LAST in the list and
  `punch` stays index 0.
- Barbarian has **no hurt or death art in either loadout**; both fall back to idle, and
  `GrayscaleDeath` is what sells the death. `staging/players/` also holds a full unshipped
  sword-and-shield loadout for the knight and an axe-less one for the barbarian — see
  `stagedNotShipped` in the manifest for what was held back and why.

## NPC animation (single-view art)

Gatita is the first NPC animated from art drawn as ONE front-facing view, cut by
`tools/atlas/wave6/build_gatita_frames.py` (6 idle frames, 8 walk) and pinned by
`GatitaAnimationDataTests`. What it took, beyond the cutting:

- **A per-STATE playback dial, because neither existing one could say "breathes slowly,
  walks normally".** The entity-wide `animationSpeedMultiplier` moves every state at once,
  so slowing her idle would have made her wade; and the per-VARIANT multiplier is answered
  by `PacingOf`, which returns the neutral default for variant -1 — which is what idle, walk
  and chase always are, since only Attack and Cast carry variants.
  `EntityAssetConfig.statePacing` is that third dial, keyed by a STRING for the same two
  reasons `LoadoutStateSheets.state` is. The three COMPOSE rather than override (a slow idle
  on a fast creature has to be both), and the state one is deliberately NOT folded into
  `PacingOf`: that method is public and answers "how is this VARIANT paced", so a caller
  sizing one action's window must not silently start being told about a state-wide dial.
  Gatita ships idle at 0.40x — a 2.25 s breath against the 0.9 s the default rate gives six
  frames, which reads as panting.
- **`BuildSet` prefers `directional` over `sheets`**, so animating an entity that already
  has static poses means CLEARING them. Leaving them silently wins and not one frame of the
  new animation ever renders.
- **Single-view art fills all eight direction buckets, and the patrol path has to be
  horizontal.** `DirectionalAnimator` never flips and `CreateSetFromLinearFrames` slices a
  list into eight CONTIGUOUS per-direction buckets — it is not one animation — so her six
  frames are repeated eight times. She then has no back to show, which is why `stroll` is a
  horizontal pace: walking north would read as moon-walking towards the camera.
- **`by_eid` is keyed by an F5 PLACEMENT id, not by monsterKey.** It beats `by_archetype`
  and looks like the surgical place to move ONE character onto a new FSM set — and for a
  spawner-produced NPC it is silently unreachable. Measured live: Gatita kept `NPC_Passive`'s
  four-state whitelist and zero transitions, so she stood still with a correct-looking
  assignments.json. Override a monster TYPE through `by_archetype`; that is already
  per-monsterKey, so it reaches her alone anyway.
- **Nothing in the state classes moves Idle to Patrol.** `IdleState`'s only coded exits are
  Unconscious and Chase, so an NPC alternating between standing and walking needs AUTHORED
  transitions both ways; `NPC_Stroller` carries them at 240 and 300 `cooldown_frames`
  (4 s idle, 5 s pacing). And the set must still declare no `ChaseState` — the whitelist is
  the ONLY thing that makes a faction peaceful, since no state class reads `stats.faction`.
- **A poll interval close to the cycle period aliases.** Sampling her state every ~8 s
  against a 9 s Idle+Patrol cycle reported PatrolState five times running and looked like a
  transition that never fired; `TimeInCurrentState` plus the live cooldown map showed it
  counting down normally. Same trap for facing: position sampled across two calls said
  "walking west while facing east", while velocity and facing read in the SAME frame agreed
  exactly. Read a state and its cause in one frame, never across two.

## Prop / building sheet pipeline

A multi-object sheet becomes placeable buildings through four stages. Each wave of sheets
writes its OWN manifest; the importer and `BuildingPropCatalogTests` read every
`building_props_manifest*.json` in the folder, so a new wave never clobbers the record of
the last (the source sheets are deleted once imported, and the manifest is what remains).

```text
slice_prop_sheet.py     sheet PNG        -> crops + <sheet>.slices.json + numbered preview
make_contact_sheet.py   crops            -> one numbered image to name the crops from
<classification>        crops            -> building_props_metadata*.json
build_building_props.py crops + metadata -> Resources/Buildings/<category>/*.png + manifest
BuildingPropImporter    manifest(s)      -> BuildingTemplateData assets + BuildingCatalog
```

- The classification is a hand-written table, not a guess: `tools/atlas/wave2/classify.py`
  holds one row per crop (`index name category split_ratio target_height_tiles [flags]`)
  and refuses to run if a name would overwrite a sprite an earlier wave shipped.
- `split_ratio` is the fraction of the sprite drawn as CANOPY, over the player —
  `BuildingObject.Assembly` computes the footprint as `spriteH * (1 - splitRatio)`. The
  ladder in use is 0.0 flat / 0.3 knee / 0.45 waist / 0.6 shoulder / 0.8 tall / 0.85 building.
- A prop that carries its own light declares `@Preset[:offsetY]` (Lamp / Torch / Magic /
  Candle — the keys `Data/LightPresetCatalog.asset` defines). The manifest carries it as
  `lightPresetKey` + `lightOffsetY`, and the importer only WRITES those fields when the key
  is non-empty — a manifest predating the field must not unlight the fixtures that were
  authored by hand.
- Sprites are resampled in PREMULTIPLIED alpha (`RGBa`); resampling straight RGBA blends the
  zeroed RGB of transparent pixels into the edges and rings every prop with a dark halo.
- `Resources/Buildings` is packed whole by `SpriteAtlases/buildings.spriteatlas`, so a new
  category folder needs no atlas wiring — but it DOES need a rule in `BuildingCategory`, or
  every template in it silently drains into the Structures tab. `BuildingCategoryTests`
  fails on exactly that.

## Chat / persona pipeline

Seven characters can be talked to: the six vendors and `Felipondor`. Their personas were
recovered from the `archive/python-legacy-2026-05-06` tag, where Valkur's Python build kept
them as `data/chat/personas/*.json` alongside an `assignments.json`, six style prompts and
twelve real conversation logs. Nothing in the dialogue is invented.

```text
tools/chat/{personas,prompts,memories}/       recovered sources (tracked)
tools/chat/build_persona_manifest.py          -> generated/chat_personas_manifest.json
Valkur > Chat > Import Personas                -> Data/ChatPersonas/*.asset
                                                  Data/ChatPersonas/Profiles/*_profile.asset
                                                  Resources/Chat/ChatAssignmentCatalog.asset
Valkur > Chat > Wire Entities To Personas      -> MonsterDefinition.chatPersona / .vendorConfig
                                                  Data/Vendor/Configs/*.asset
```

- **A persona is two assets.** `NPCPersonaDefinition` is what the runtime consults when a
  conversation opens — range, greeting, dialogue lines, discount caps.
  `PersonaProfileDefinition` is the prose (background, speech, boundaries, lore, moods) read
  only by the prompt builder. Splitting them keeps the 90 % of sessions with no language
  model from deserialising a page per NPC, and keeps a chat range from being buried under a
  paragraph. `PersonaProfileTests` fails if the two disagree on `personaId`.
- **A greeting is only taken from a transcript when it names the character.** The archived
  logs are contaminated — Roberto the mage opens one of his with Pavel the lumberjack's line
  about fresh timber — and they interleave the character's voice with stock and receipt lines
  the Python shop emitted ("Tengo 0 de madera a 1 oro la unidad."). Requiring the display name
  makes cross-persona leakage structurally impossible. Dialogue lines are authored material
  ONLY (humour, small talk, negotiation, catchphrases, style-prompt examples) for the same
  reason: a line spoken by the wrong character is worse than one line fewer.
- **`MonsterDefinition.chatPersona` is what makes an entity talkable**, and the join key to
  the catalogue is `displayName` ("Gatita", "Felipondor"), the same key Python used. The
  runtime prefers the direct reference on `NPCChatIdentity`, added at spawn by
  `EntitySetup.ConfigureChat`; the by-name catalogue is the fallback for hand-placed entities.
  A rename can no longer unhook an NPC's dialogue.
- **Abigail has no `vendorConfig` on purpose.** She is a banker whose persona offers "cofres
  seguros y certificados de depósito", and no such item exists in `ItemCatalog`. A Trade
  button over an empty shop reads as a bug, so she simply talks. The other five are seeded
  from `ItemDefinition.itemType`, which already carried exactly these five trades.
- **The importer fills only empty fields.** Re-running it never overwrites a greeting or a
  line a designer rewrote in the Inspector — same "creation defaults, authored value wins"
  contract `TilesetRulesetImporter` uses. `Import Personas (Overwrite Authored)` is the
  escape hatch. Neither uses `Undo.RecordObject`, for the reason the building-template note
  in the gotchas records.

## Player stats and progression

Everything numeric about the player composes in ONE place, and everything the player
buys lands in one of that place's layers.

```text
PlayerStats            the store. base + level + skill + grimoire + equipment + buff + aura
  ↑ writes its own layer, never the total
  ├── PlayerProgression   orchestrator: resolves the catalog, grants both currencies, rebuilds layers
  │     ├── LearnedSkills   talents, per class, RANKED, bought with skill points
  │     └── KnownSpells     grimoire, per school, bought with arcane points — and the single
  │                         answer to "may this character cast X"
  ├── EquipmentStatSource   rebuilt on every inventory change
  └── TimedBuffSource       potions and shrines, keyed so a second flask REFRESHES
  ↓ pushes resolved values out
Health · Mana · MeleeCombat · PlayerController · Experience
```

- **Every source writes only its own layer.** Same rule as `SpriteTintStack`, for the same
  reason: nine systems each caching a value, changing it and writing the cache back is
  correct alone and wrong together. Removal is then exact by construction — unequipping a
  sword removes the sword's +6 and nothing else, with no "restore the original" step.
- **Composition is published and fixed:**
  `final = clamp((base + Σ Flat) × (1 + Σ PercentAdd) × Π (1 + PercentMult))`.
  `PercentAdd` pools (ten +5 % nodes give +50 %); `PercentMult` is its own factor and is
  reserved for capstones. Folding them into one bucket is the classic ARPG bug where late
  additive stacking makes every other source worthless.
- **The push must be IDEMPOTENT**, which is why `Health.SetMaxHp` / `Mana.SetMaxMana` /
  `MeleeCombat.SetDamage` exist beside the old delta APIs. `IncreaseMaxHp` cannot be called
  from a recompute: recomputing on an unrelated buff expiring would grant the bonus again
  and heal the player a little each time.
- **Two trees, two currencies, on purpose.** A talent is a NUMBER and per class; a spell is
  a VERB and shared by all five classes. One tree makes "+5 % melee damage" compete with
  "unlock Meteor Shower", which is not a real choice, and a per-class copy of the spell
  graph would be five assets drifting apart on the first retune. Class identity in the
  grimoire is `SpellTree.classAffinities` plus an off-affinity surcharge — a tendency, not
  a wall.
- **`StatKind` is closed and every value must have a consumer.** `PlayerStatsWiringTests`
  walks the enum against `PlayerStats.Consumers.cs` and fails on any value that reaches no
  component. That test is the whole point: it is what stops this becoming the twelfth
  authored-and-inert layer beside `animation_map.json`, the FSM's `Actions` block and the
  four casting flags nothing reads.
- **Spells are earned now.** `EntitySetup` still registers the whole catalogue, then
  `PlayerProgression.SyncSpellBook` REPLACES the book with exactly what the character
  knows — measured live, 77 registered spells become 2 on a fresh dwarf. Replacement, not
  addition, because a respec has to take spells away. The F4 Spells Editor lifts it through
  `SpellCaster.SetAuthoringUnlockAll` and re-registers the catalogue while it is open;
  without that the editor could select any spell and cast only the handful the character
  knows, and the nineteen `AnimationProbe` spells would be unreachable.
- **Console:** `stats [name]`, `sp <n>`, `ap <n>`, `learn <id>`, `respec [skills|spells|all]`,
  `grimoire [school]`. `stats` prints the per-layer breakdown, which is what makes the whole
  layer testable from `execute_code` without a UI.
- Generated content: `Valkur > Progression > Seed Progression Content` CREATES what is
  missing and never overwrites — the same "creation defaults, authored value wins" contract
  `TilesetRulesetImporter` and the persona importer use. The overwrite variant is a separate
  menu item behind a confirmation. Neither uses `Undo.RecordObject`, for the reason the
  building-template note in the gotchas records.

## The FSM is two machines, and only one of them is authored

A monster's state graph has two owners and they do not overlap:

- **Authored** — `StreamingAssets/FSM/sets.json`, edited in F12. Supplies the initial state, the
  allowed-state vocabulary (which becomes `StateMachine.SetAllowedStates`) and a handful of
  transitions. `FleeState` and `AlertChaseState` are reachable ONLY from here: grep returns zero
  `new FleeState(` / `new AlertChaseState(` sites in the whole project.
- **Coded** — 24 `fsm.ChangeState(new X())` edges inside the state classes, plus the flinch and
  death edges raised by `StateMachine`'s event queue and the cast edge pushed by `NPCAutoCast`.
  These own every real decision: aggro acquisition, melee entry, de-aggro, leash, corpse timer.

Only the authored half was ever drawn, so F12 showed three edges of a machine that has 27.
`FSMBuiltInTransitions` now declares the coded half and the graph renders it dimmed and locked;
`FSMBuiltInTransitionRegistryTests` scans the state classes for every `ChangeState(new X())` and
fails when the table and the source disagree **in either direction**, which is what stops the
table becoming another `animation_map.json`. Adding a `ChangeState` call without declaring it
is a red test, by design.

Consequences worth knowing before editing any of it:

- **A set's node list is a whitelist, and deleting a node deadlocks silently.** A refused
  `ChangeState` used to return with no log at all; it now warns once per `From>To` pair.
  `DeathState`, `DamageState` and `UnconsciousState` bypass the whitelist by hardcoded name, so
  deleting them changes nothing.
- **The whitelist is the only thing that makes a faction peaceful.** No state class reads
  `stats.faction` — zero occurrences in Idle/Patrol/Chase/Attack. Vendors used to be harmless
  only because `aggroRange` was 0; raising it would have made them hunt the player. `NPC_Passive`
  declares no `ChaseState`, so the acquisition is refused structurally.
- **An authored edge with an empty guard is UNCONDITIONAL, not inert.** `FSMCondition.Parse("")`
  returns null and `StateMachine` treats a null condition as pass. `Parse` validates the SHAPE of
  a clause, never the NAME of a signal: an unknown term falls through to `GetContextFloat(term, 0f)`,
  so a misspelled `hp_pctt < 0.25` compares `0 < 0.25` and fires forever.
- **`cooldown_frames` is seconds x 60**, divided by a hardcoded 60 at load — and `AppliesTo` is
  tested before the cooldown, so the clock only advances on ticks spent in the edge's `from` state.
- **`Actions`, `Blackboard` and per-state `props` round-trip to disk and reach no runtime code.**

## Incident reports

Past incidents that left investigation hooks behind. Read these first when a
related symptom reappears.

| Incident | When | Doc |
|---|---|---|
| F10 Buildings save collapses `rel_x`/`rel_y` to one position per zone | 2026-05-08 (mitigated, root cause TBD) | `.github/incidents/BUILDINGS_SAVE_POSITION_COLLAPSE.md` |
| Run "twin-save" — duplicate `Saves/<runId>/` folders with byte-identical body but distinct `meta.run_id` | 2026-05-08 (mitigated — root cause: EditMode test pollution; fixed by `RefuseWriteOutsidePlayMode` guard) | `.github/incidents/RUN_TWIN_SAVE.md` |
| Spawners drift by their zone's origin on every restart (save wrote absolute world coords into a zone-relative field) | 2026-08-19 (fixed) | `.github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md` |
| 216 building templates (ids 4–313) deleted from the working tree; catalog rewritten without them | 2026-09-04 (recovered, root cause TBD) | `.github/incidents/BUILDING_TEMPLATES_MASS_DELETION.md` |

## Open work

- **Editor UI/UX unification & persistence** — audited 2026-09-02, layer shipped 2026-09-03.
  The sixteen editors are 319 files / ~77.6k LOC and drifted: three (Camera, DungeonNodeGraph,
  General) carry NO chrome at all, `PanelChrome` is missing from six, the tutorial overlay from
  eight, `EditorCameraZoomController` from ten — and **459 raw `new Color(`** literals sit
  outside the theme (Map 90, Tile 85, Spells 68). Nothing persisted between sessions except
  hidden table columns, in three editors, via three copied PlayerPrefs implementations.
  **Phase 1 is done**: `Valkur.Core.Editors` holds the contracts + DTOs (`EditorWorkspace`,
  `EditorPanelState`, `EditorSelectionRecord`, `IProvidesWorkspaceState`, `IPanelStateSink`),
  `JsonEditorWorkspaceStore` writes one document per editor under
  `persistentDataPath/EditorWorkspace/`, `DraggablePanel` gained `CaptureState`/`ApplyState`
  plus an `Owner` that namespaces its persistence key, and `EditorWorkspaceService` hooks
  `GameEditorManager.OpenExclusive` / `NotifyDeactivated` — ONE seam, not sixteen. Pinned by
  `EditorWorkspaceContractTests` (16 tests). **Items (F7) is the first adopter** (2026-09-03):
  it implements `IProvidesWorkspaceState` and remembers mode, category tab, search text,
  hidden columns, the picked catalog item and the selected world drop — and its
  `TableColumnsConfig` PlayerPrefs entry is gone, the first of the three duplicates to go.
  **Tile (F8) followed the same day** and earned its place as the hard pilot: it exposed a
  real defect in the layer — a self-closing editor was captured TWICE per close, and the
  second pass ran after `Deactivate` had cleared `_state.SelectedCellPos`, writing that null
  over the good snapshot. `GameEditorManager` now de-duplicates per close. **Phase 3 landed the same day: all 15 registered editors implement
  `IProvidesWorkspaceState`**, and all three duplicated `PlayerPrefs` column stores (Items,
  Particles, Spells) are gone. No editor reopens in a destructive mode — Buildings refuses
  to restore Delete/Erase, Entities Delete, Inventory DeleteItem, Tile its collider and
  layer-jump paint modes — because opening straight into one is how an author destroys
  something they only meant to look at. What each editor deliberately does NOT persist is
  a decision with a reason recorded per editor in the roadmap (Lighting's ambient override,
  Spawners' instance selection, FSM's set, TimeWeather's world state, DungeonNodeGraph's
  nodes). **Phase 4 landed 2026-09-03** and corrected the audit that started it. Zoom: every one of
  the 11 editors that can pan the world can now zoom it (`EditorCameraZoomController` was in
  3), and it is safe to spread because it steps through `CameraSetup.ComputeEditorZoomNext`,
  which stays on the PPU ladder `SnapOrthoSize` maintains. Theme: 79 of the 459 raw
  `new Color(` literals became tokens — including three NEW ones for things copied verbatim
  across editors (`SCROLL_TRACK` + `SCROLL_HANDLE`, the same scrollbar in five editors, and
  `DANGER_IDLE`, a destructive button at rest, in nineteen sites). The remaining 387 are
  held by `EditorRawColorRatchetTests` against `Tests/EditMode/Baselines/editor-raw-colors.txt`:
  a per-file count that may fall freely and may never rise. A blanket rewrite was rejected on
  measurement — only 37 of 421 literals matched a token exactly, so the rest would have been
  guessing at a designer's intent. **Chrome: the audit was wrong.** It counted whether an
  editor NAMES `DraggablePanel`/`PanelChrome`, but six reach both through
  `EditorUIHelpers.MakeDropPanel`, so the editors it marked worst were the ones doing it
  right — 15 of 16 have real chrome, and the one gap is DungeonNodeGraph, a deliberate
  full-screen slab whose layout is a separate design decision (its private palette, which
  had drifted from the kit, now comes from `UITheme`). Full audit, architecture, selection
  policy and acceptance criteria: `.github/EDITOR_UX_AUDIT_AND_ROADMAP.md`.

- **Multi-map Phase B/C** — Phase A (per-slot persistence routing) shipped 2026-08-18: buildings, spawners, lights, particles and authored item drops each own their file per map slot. Still open: built-in parallel worlds (Sky / Hell) and cross-world portals at runtime. See `.github/MAP_EDITOR_MULTIMAP_ROADMAP.md`.
- **Asset pipeline Phase 2** — finalised `asset_map.csv` schema + the formal naming convention. Bulk reimport already executed; `ValkurAssetPostprocessor` writes Uncompressed platform overrides. Atlas consolidation is **done** (2026-08-18): exactly 9 atlases, all under `_Project/SpriteAtlases/`, one owner (`SpriteAtlasBuilder`).
- **Day/night overhaul** — audited 2026-08-25 at **2.0/10**; Phases 0-3 shipped the same day, now **6.4/10**. The cycle used to reach no rendered pixel: three wrong URP enum literals (URP 14: `Freeform=1, Sprite=2, Point=3, Global=4`) left the scene light a `Point` of radius 1 and every placed torch a cookie-less `Sprite` light, while `WorldGridBuilder` forced the whole world to `Sprite-Unlit-Default` unconditionally. Now: typed URP API in all three light paths; world and entities lit (`Valkur/SpriteHDRTintLit`); placed lights on blend style **1 (Additive)**; colour from an 8-key Gradient in `Resources/DayNightProfile.asset`; the `Buildings/lights/` prop family emits its own light via `BuildingTemplateData.lightPresetKey` + `WorldLightLoader.RegisterDerivedLight` (derived lights are `persistent = false`, so `SaveAll` never writes them to `light_instances.json`); and a **`ScreenGradeFeature`** renderer feature on `Renderer2D.asset` does per-phase saturation/contrast/vignette/dither in one blit at a measured **0.215 ms/frame** — it does NOT need `renderPostProcessing`, so the ~18 ms UberPost stack stays off. Single owners: `AmbientLitSortingLayers` (light mask), `Core/Rendering/WorldSpriteMaterials` (lit vs unlit), `ScreenGradeSettings` (the live grade; static because Core cannot reference Gameplay). **URP 2D shadows render correctly but are disabled**: measured 11 % of pixels changed with a valid probe, yet URP derives the caster shape from the `Renderer` bounds, so every building throws a hard rectangular wedge. Accurate silhouettes would need the painted collision grid as caster geometry. NOTE `ShadowCaster2D.IsLit` reads `light.boundingSphere.radius`, written only by `Light2D.LateUpdate` — a light created and rendered in the same call has radius 0 and measures a false zero. Still open: atmosphere (3.0) and gameplay coupling (0.0), plus persisting the time of day and the F2 editor's authoring. **The phases are pinned by 40 tests** across `DayNightPhaseLookTests` (reads the shipped `Resources/DayNightProfile.asset`, asserts characteristics not literals), `DayNightPipelineWiringTests` (the URP enum constants, exactly one Global light, the sorting-layer mask vs the layers that go lit, blend style 1 still Additive, the ScreenGrade feature still installed) and `TimeWeatherPhaseShortcutTests` (each F2 phase button's hour, label and the phase the cycle actually reports there). Full findings and the roadmap: `.github/DAY_NIGHT_AUDIT_AND_ROADMAP.md`.
- **Weather (Wind / Rain / Snow)** — rebuilt 2026-08-30, zone-scoped 2026-09-01, in
  `Scripts/Gameplay/World/Weather/`. Weather is **stored per ZONE and rendered once**:
  `WeatherManager` holds a `zone -> levels` table and drives ONE set of effects at whatever
  the player's current zone asks for, so crossing a boundary retargets the existing effect
  and the fade turns that into a ramp. F2 authors the zone the player is standing in and
  writes its name at the top of the panel; indoors (`ZoneManager.IsDetectionSuspended`) the
  rows go inert and the weather fades — you are under a roof. Console: `weather`,
  `weatherzones`, `weatherin <zone> ...`, `weather clear [all]`.
  Each effect is a stack of `WeatherLayer` depth slices on shared `ParticleMaterialCache`
  materials with procedural textures (`WeatherTextures`), driven by one shared gust field
  (`WeatherWind`, ticked once per frame by `WeatherManager` so every reader samples the same
  gust) and one shared screen-grade/lightning owner (`WeatherGrade`, composed into
  `DayNightCycle.PublishScreenGrade`; the strike also boosts the Global Light 2D so it lights
  the world rather than only the post-process). Levels are **Off / Light / Medium / Heavy**
  (`WeatherIntensity`) — activation (fade) and density (level) are separate scalars, so
  raising a live weather ramps it instead of restarting it. Wind + Rain is a real composition:
  the wind effect raises `WeatherWind.WeatherSpeed`, which rain and snow slant with, and Heavy
  rain arms lightning. Audio is **synthesised** (`WeatherAudio` — filtered noise beds; the
  project ships no ambient recordings and every subclass had left `ResolveAudioClip` returning
  null since the class was written, so weather had always been silent); snow is silent on
  purpose. Snow also **accumulates on the world**, per landed flake: every expiring flake
  stamps `SnowSplatMap` (a camera-following world-space R8 buffer) and emits one settled
  speck where it stopped, while `SnowAccumulation` keeps the global depth clock. The two
  MULTIPLY in `Shaders/ValkurSnow.hlsl` — the map says which ground has a drift, the scalar
  says how deep a drift can get — and the result is a blanket on Ground/FloorDecals and a
  silhouette-following cap that grows DOWNWARD from the roof line on everything else. All 1176
  building templates and every generated tile pack collect snow with no snow art authored
  anywhere, and it melts on a phase-dependent clock (Day 3.2x, Night 0.25x). Authored from **F2 → Weather** (a row click
  cycles the level) or the `weather` / `wind` / `lightning` / `snow` console commands. Still
  open: authored climates per zone on disk (the table is session state today), weather that
  evolves off-screen, blending a zone's weather by DISTANCE to its boundary rather than
  switching on entry, lying snow that belongs to a zone rather than to the camera-following
  buffer, rain wetting surfaces the way snow covers them, and gameplay coupling (wet-ground
  friction, visibility).
- **Building doors & interiors** — shipped 2026-08-26. A placed building can declare a
  doorway and lead somewhere: `hasDoor` + a normalized anchor on `BuildingTemplateData`,
  `overrides.door` per instance, `BuildingDoor` (a poll, not a trigger) parented to the
  building, `WorldTransitionService` as the single owner of the world swap, and an
  `InteriorExit` dropped on the arrival tile that arms once the player steps away. Authored
  from **F10 -> Door** or from the `door` / `doors` / `overlays` / `leave` console commands,
  both through the same `BuildingsRuntimeEditor.TrySetDoor` seams. Working example: building
  ID 64 (`houses/curse_house_topdown`) leads to
  `Maps/Interiors/house_interior_small.overlay.json`. Still open: interiors are bare rooms
  (no furniture, NPCs, loot or lighting), one file per doorway, no nesting, and no press-to-
  enter until the `E` double-binding is resolved. See `.github/BUILDING_DOORS_ROADMAP.md`.
- **Boss music tracks** — the wiring is done (`BossDefinition.Phase.musicTrackId` → `BossConfigurator.ApplyPhaseMusic` → `IAudioService.PlayMusicByTrackId`, with `BossPhaseAudio` as the inspector-authored alternative). What remains is **data**: no boss-specific track exists in `AudioCatalog.asset` yet, so `SampleBoss.asset` leaves `musicTrackId` empty.

The **`Valkur.Infrastructure.Persistence.Profile`** layer (run history, kill stats, achievements, profile counters, statistics HUD) lives behind `IProfileDb` (`JsonProfileDb` today; SQLite ready as a drop-in once row counts justify it) — see `.github/SQLITE_MIGRATION_AUDIT.md`.

## graphify

This repository ships a persistent structural knowledge graph in `graphify-out/`
(31.5k nodes / 78.4k edges over 2013 code files, built from the C# + Python AST —
no LLM, no API key, nothing leaves this machine). It is the cheapest way to answer "how does this fit
together" questions without reading dozens of files.

### Use graphify FIRST for

architecture · dependencies · class/module relationships · call paths · who owns a
piece of functionality · locating an implementation you cannot name yet ·
understanding an unfamiliar system · impact analysis before a refactor.

Then open **only** the specific `file:line` results graphify hands back.

### Do NOT reach for graphify when

you already know the file, the edit is a few lines, you are reading a file end to
end, or you are grepping for a literal string / asset name. Reading the code
directly is cheaper and more accurate there. The graph is an index, not the truth —
for exact current implementation detail, read the file.

### Commands

```bash
graphify query "<question>"        # BFS subgraph for a question (add --budget N to cap output)
graphify explain "<Symbol>"        # one node, its neighbours, grouped by file — best single tool
graphify affected "<Symbol>"       # reverse traversal: what breaks if I change this
graphify path "<A>" "<B>" --undirected   # relationship between two components
graphify god-nodes --top 15        # architectural hubs
graphify update .                  # incremental re-extract after code changes (AST only, free)
```

Notes measured on THIS repo:

- `explain` is the highest-signal command. `query` is broad — a question like
  "what depends on the inventory system" returns 400+ nodes and truncates against
  the token budget, and test files crowd the head of the list. Prefer `explain` /
  `affected` when you can name a symbol.
- **Ambiguity is the normal case here**, because Valkur splits classes across
  partials (`BuildingLoader.cs` + `BuildingLoader.Spawning.cs`). `explain "X"`
  answers `Ambiguous: 'X' matches 2 nodes` **and prints the candidate node ids** —
  re-run `explain` / `affected` with the full id. `affected` alone just says
  `No unique node match`, so when that happens, go through `explain` first to get
  the id.
- `path` over directed edges often finds nothing; `--undirected` works but can
  route through hubs like `MonoBehaviour`, which is a true edge and a useless
  answer. Treat `path` as the weakest of the four.
- Communities are named after their most central symbol (e.g. `BuildingLoader`,
  `RoomNodeTypeSO`), not `Community N`.
- `.graphifyignore` (repo root, tracked) is what keeps `unity/Udemy_Inspiration/`
  and the vendored VFX demo scripts out of the graph. It matters because
  **`graphify update` does not read `.gitignore`** — without it, an incremental
  update silently pulls 3.7k C# files of the read-only reference project into the
  graph. Verified: 0 nodes from `unity/Udemy_Inspiration/`.
- `Scripts/Data/Dungeon/Udemy/` IS ours (namespace `Valkur.Data.Dungeon.Udemy`) and
  is correctly in the graph — do not confuse it with the excluded reference project.

### Vistas generadas (todas en `graphify-out/`, todas gitignored)

| Fichero | Qué es | Comando que lo regenera |
|---|---|---|
| `graph.html` | Grafo interactivo 2D (vis-network). Agregado por COMUNIDAD, no por nodo, porque el grafo pasa de 5000 nodos. Carga la librería de `unpkg.com`: necesita internet | `graphify cluster-only . --no-label` |
| `GRAPH_TREE.html` | Árbol colapsable por carpeta/fichero/símbolo. Es la mejor vista para recorrer la estructura real del repo | `graphify tree --label "Valkur"` |
| `RogueLike-callflow.html` | Diagramas Mermaid por sección. Las secciones 10-15 son de Valkur; las 2-9 son plantilla genérica de graphify sobre sí mismo, ignóralas | `graphify export callflow-html --lang en` |
| `wiki/index.md` | 969 artículos, uno por comunidad, ordenados por tamaño. Entrada de navegación en texto plano — barato de leer para un agente | `graphify export wiki` |

No hay vista 3D. `export svg` existe pero necesita `matplotlib` y produce un hairball estático de 31k nodos.

### MCP

`.mcp.json` also registers graphify as a stdio MCP server (tools: `query_graph`,
`get_node`, `get_neighbors`, `get_community`, `god_nodes`, `graph_stats`,
`shortest_path`). Either surface is fine; the CLI is the fallback if the MCP server
is not approved in this session. Argument names differ from the CLI: `get_node` and
`get_neighbors` take **`label`** (not `node`), `query_graph` takes `question`,
`shortest_path` takes `source` / `target` / `undirected`.

### Keeping the graph fresh

- Small / normal code changes -> `graphify update .` (incremental, seconds).
- Large structural change, mass rename or deletion -> `graphify update . --force`,
  then `graphify cluster-only . --no-label` to refresh `GRAPH_REPORT.md` + `graph.html`.
- A full rebuild is `graphify extract . --code-only` (~5 min) and is rarely needed.
- **Do not rebuild at the start of a session.** `graphify-out/` persists on disk;
  just use it. Rebuild only after real code churn.
- `graphify-out/` is gitignored (`graph.json` is 66 MB and derived).
