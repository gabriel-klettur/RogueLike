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
| `Spells/Visuals/` | ElementalProjectileVisual, FireballVisual, FireballImpactFX, LightningBoltFX, MeteorMissileFX, AreaFXRig |
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
| Buildings | `Data/Catalogs/Buildings/BuildingCatalog.asset` (edit via F10 in-game) — 969 templates; every prop imported through the sheet pipeline is described by a `tools/atlas/generated/building_props_manifest*.json`, one per wave |
| Particles | `Data/Catalogs/Particles/ParticlePresetCatalog.asset` (edit via F1) |
| Spawners | `Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset` (edit via F3) |
| Camera feel (shake, kick, lead, smooth follow) | `Resources/CameraFeelProfile.asset` |
| Lighting Presets | `Data/LightPresetCatalog.asset` (edit via Ctrl+F3) |
| Chat Personas / Assignments | `Data/ChatPersonas/*.asset` + `ChatAssignmentCatalog.asset` |
| Vendors | `Data/Vendor/{EconomyGroups,Configs}/*.asset` |
| Players | `Data/Catalogs/Players/*.asset` |
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
| `editor-ux-parity` | Audit / enforce UI/UX parity across in-game runtime editors |
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

## Player character pipeline (2 directions)

`dwarf`, `barbarian` and `elven` are built from **side-view art drawn facing right, in one
direction**, and mirrored. `mague` and `valkyrie` still run on the legacy 8-direction
strips. The two pipelines coexist on purpose and have different owners:

| | wave3 (dwarf, barbarian, elven) | legacy (mague, valkyrie) |
|---|---|---|
| Source | `staging/players/<char>/` (gitignored, repo root); elven is `elf_wave4/` | `Art/Characters/<key>/<key>_<state>.png` |
| Cutter | `tools/atlas/wave3/build_player_frames.py` | — |
| Binder | `PlayerFramesImporter` (`Valkur > Players > Import Frame Sheets`) | `PlayerCharacterAssetBinder` (`Valkur > Setup > Rebuild Player Character Assets`) |
| On disk | one tightly-cropped PNG per frame, `<key>_<state>_<r\|l><i>.png` | one 5120x128 strip per state, 128 px cells |
| Record | `tools/atlas/generated/player_frames_manifest_wave3.json` | — |

```text
slice_prop_sheet.py --all --sheet-dir staging/players/<char> --out <slices>
wave3/build_player_frames.py <slices>     # align, scale, mirror, write manifest
Valkur > Players > Import Frame Sheets (Dry Run) then (Apply)
```

- **The mirrored half is baked as its own sprite.** `DirectionalAnimator` never flips —
  `ChaseState` says so — so the importer fills all eight buckets from two. Each state's list
  is `framesPerDirection * 8` and repeats each sprite four or five times. `knight_red`
  already shipped this way.
- **Which half is the mirror has to be MEASURED, and this wave's art faces WEST.** All three
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
- **`build_player_frames.py` scales each state off FRAME 0's foot-to-crown height.** Every
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
- Barbarian has **no hurt or death art in either loadout**; both fall back to idle, and
  `GrayscaleDeath` is what sells the death. `staging/players/` also holds a full unshipped
  sword-and-shield loadout for the knight and an axe-less one for the barbarian — see
  `stagedNotShipped` in the manifest for what was held back and why.

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

## Open work

- **Multi-map Phase B/C** — Phase A (per-slot persistence routing) shipped 2026-08-18: buildings, spawners, lights, particles and authored item drops each own their file per map slot. Still open: built-in parallel worlds (Sky / Hell) and cross-world portals at runtime. See `.github/MAP_EDITOR_MULTIMAP_ROADMAP.md`.
- **Asset pipeline Phase 2** — finalised `asset_map.csv` schema + the formal naming convention. Bulk reimport already executed; `ValkurAssetPostprocessor` writes Uncompressed platform overrides. Atlas consolidation is **done** (2026-08-18): exactly 9 atlases, all under `_Project/SpriteAtlases/`, one owner (`SpriteAtlasBuilder`).
- **Day/night overhaul** — audited 2026-08-25 at **2.0/10**; Phases 0-3 shipped the same day, now **6.4/10**. The cycle used to reach no rendered pixel: three wrong URP enum literals (URP 14: `Freeform=1, Sprite=2, Point=3, Global=4`) left the scene light a `Point` of radius 1 and every placed torch a cookie-less `Sprite` light, while `WorldGridBuilder` forced the whole world to `Sprite-Unlit-Default` unconditionally. Now: typed URP API in all three light paths; world and entities lit (`Valkur/SpriteHDRTintLit`); placed lights on blend style **1 (Additive)**; colour from an 8-key Gradient in `Resources/DayNightProfile.asset`; the `Buildings/lights/` prop family emits its own light via `BuildingTemplateData.lightPresetKey` + `WorldLightLoader.RegisterDerivedLight` (derived lights are `persistent = false`, so `SaveAll` never writes them to `light_instances.json`); and a **`ScreenGradeFeature`** renderer feature on `Renderer2D.asset` does per-phase saturation/contrast/vignette/dither in one blit at a measured **0.215 ms/frame** — it does NOT need `renderPostProcessing`, so the ~18 ms UberPost stack stays off. Single owners: `AmbientLitSortingLayers` (light mask), `Core/Rendering/WorldSpriteMaterials` (lit vs unlit), `ScreenGradeSettings` (the live grade; static because Core cannot reference Gameplay). **URP 2D shadows render correctly but are disabled**: measured 11 % of pixels changed with a valid probe, yet URP derives the caster shape from the `Renderer` bounds, so every building throws a hard rectangular wedge. Accurate silhouettes would need the painted collision grid as caster geometry. NOTE `ShadowCaster2D.IsLit` reads `light.boundingSphere.radius`, written only by `Light2D.LateUpdate` — a light created and rendered in the same call has radius 0 and measures a false zero. Still open: atmosphere (3.0) and gameplay coupling (0.0), plus persisting the time of day and the F2 editor's authoring. **The phases are pinned by 40 tests** across `DayNightPhaseLookTests` (reads the shipped `Resources/DayNightProfile.asset`, asserts characteristics not literals), `DayNightPipelineWiringTests` (the URP enum constants, exactly one Global light, the sorting-layer mask vs the layers that go lit, blend style 1 still Additive, the ScreenGrade feature still installed) and `TimeWeatherPhaseShortcutTests` (each F2 phase button's hour, label and the phase the cycle actually reports there). Full findings and the roadmap: `.github/DAY_NIGHT_AUDIT_AND_ROADMAP.md`.
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
