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
| Buildings | `Data/Catalogs/Buildings/BuildingCatalog.asset` (edit via F10 in-game) |
| Particles | `Data/Catalogs/Particles/ParticlePresetCatalog.asset` (edit via F1) |
| Spawners | `Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset` (edit via F3) |
| Camera feel (shake, kick, lead, smooth follow) | `Resources/CameraFeelProfile.asset` |
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
- **Boss music tracks** — the wiring is done (`BossDefinition.Phase.musicTrackId` → `BossConfigurator.ApplyPhaseMusic` → `IAudioService.PlayMusicByTrackId`, with `BossPhaseAudio` as the inspector-authored alternative). What remains is **data**: no boss-specific track exists in `AudioCatalog.asset` yet, so `SampleBoss.asset` leaves `musicTrackId` empty.

The **`Valkur.Infrastructure.Persistence.Profile`** layer (run history, kill stats, achievements, profile counters, statistics HUD) lives behind `IProfileDb` (`JsonProfileDb` today; SQLite ready as a drop-in once row counts justify it) — see `.github/SQLITE_MIGRATION_AUDIT.md`.
