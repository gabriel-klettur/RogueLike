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
4. **Edit ScriptableObjects, not external JSON.** Catalog data lives in `.asset` files and is edited via the Inspector (or via the in-game runtime editors, all reached from the General Editor on **Escape** — the F-key toggles were retired, see "Editors are reached from Escape" below). World-state JSON under `StreamingAssets/` is written by the runtime editors via the `IRepository` pattern — don't hand-edit it.
5. **Never read `Mouse.current` / `Keyboard.current` / `UnityEngine.Input.*` directly outside the Input core helpers.** Use the centralized fachadas — see "Input pipeline" below.

## Input pipeline (single source of truth)

Every input read in Valkur goes through one of four centralized helpers. Touching `Mouse.current` / `Keyboard.current` / `UnityEngine.Input` directly anywhere else is a regression: it breaks under the recurring Unity 2022.3 Editor "InputSystem drops events" bug.

| Helper | Location | Use for |
|---|---|---|
| **`InputService`** | `Scripts/Core/Input/InputService.cs` | Bindings — exposes `UI.Click`, `Gameplay.Move`, `Editors.ToggleTile`, etc. from the canonical `ValkurInputActions.inputactions` asset. THIS is the binding source of truth. |
| **`MouseInputManager`** | `Scripts/Core/Input/MouseInputManager.cs` | Mouse buttons + position + wheel. `IsLeftMouseButtonPressed()`, `WasLeftMouseButtonReleasedThisFrame()`, `GetScreenMousePosition()`, `GetMouseWheelDelta()`, etc. ORs new InputSystem with legacy `UnityEngine.Input` automatically. |
| **`KeyboardInputManager`** | `Scripts/Core/Input/KeyboardInputManager.cs` | Keyboard keys. `WasKeyPressedThisFrame(Key, KeyCode)`, `IsCtrlHeld()`, `WasEnterPressedThisFrame()`, `WasEscapePressedThisFrame()`, etc. Same OR-fallback pattern. |
| **`InputCompat`** | `Scripts/Core/Input/InputCompat.cs` | Semantic menu helpers — `NavUpPressed()`, `ConfirmPressed()`, `CancelPressed()`. Wraps `KeyboardInputManager`. |
| **`EditorHotkeyBindings`** | `Scripts/Core/Input/EditorHotkeyBindings.cs` | The `Editors` map's hotkeys — Escape, backquote, Ctrl+F5/F9, the Ctrl/Alt modifier probes, F1 for the debug HUD, plus the thirteen editor toggles that now ship UNBOUND. Stateless API: `WasPerformedThisFrame(Hotkey.ToggleTile)`. Resolves the live action from `InputService.Editors` on every call (immune to zombie-after-hot-reload) and derives its legacy half from that binding. |
| **`InputBindingResolver`** | `Scripts/Core/Input/InputBindingResolver.cs` | The OR-gate itself. `WasPerformedThisFrame(action)` / `IsPressed(action)` / `WasReleasedThisFrame(action)` / `ReadVectorFallback(action)` — derives the LEGACY half from the action's own live binding, so a rebind moves both. Prefer it over a bare `action.WasPerformedThisFrame()` plus a literal `KeyCode`. |

### The binding layer (one model, not two)

There used to be TWO, and only one of them was read. `ValkurInputActions` is what gameplay
reads; a wall of `GameSettings.*KeyA` strings was what the Controls panels in the main and
pause menus wrote. Exactly twelve editor F-keys were bridged between them (`EditorBindingsApplier`,
slot 0 only) and **every gameplay field had zero production readers** — `moveUpKeyA`,
`dashKeyA`, `spell1KeyA`..`spell4KeyA`, `primaryAttackMouse`, `pauseKeyA`,
`toggleInventoryKeyA`, verified by grep. A player could rebind their movement, watch the panel
update, save it, and change nothing. All of that is deleted; the asset is the only model.

| Piece | Location | What it owns |
|---|---|---|
| `InputControlPaths` | `Core/Input/` | The translator: `path ↔ Key ↔ KeyCode ↔ cap label`, 110 controls. A static TABLE, not a device query, so EditMode tests can use it. |
| `InputActionCatalog` | `Core/Input/` | The MEANING of each action — category, context mask, `OwnerEditor`, `PayloadKey` (the spellKey), and `ReachesDamage`. A CLOSED table: an action in the asset with no descriptor is a red test. |
| `InputContexts` | `Core/Input/` | The context vocabulary and the single answer to "which is live now". |
| `InputContextPolicy` | `Core/Input/` | The Peace whitelist and the live per-action context masks. |
| `EditorInput` | `Core/Input/` | The verbs every editor shares, plus `Tool(map, action)` for one editor's own. |
| `InputBindingResolver` | `Core/Input/` | Resolves an action's live bindings and answers the OR-gate with them. Caches; call `Invalidate()` after any rebind. |
| `InputBindingStore` | `Core/Input/` | Persists binding overrides + stance masks to `persistentDataPath/Input/controls.json`. Applied by `RuntimeInputBootstrap` on boot and on every scene load. |
| `InputConflictScanner` | `Core/Input/` | Finds controls more than one action answers to, **map-aware and stance-aware**. |
| `ControlsRuntimeEditor` | `Gameplay/Editors/Controls/` | The drawn keyboard + mouse. No hotkey; opened from the General Editor (ESC → Controls), same reasoning as the Camera editor. Binds keyboard AND mouse, per binding SLOT, and can clear one. |
| `KeyboardLayoutModel` / `ControlsKeyboardView` / `ControlsMouseView` | `Gameplay/UIKit/Controls/` | The board, in UIKit so both the editor and the menus can host it. |

### Contexts: the postures are for PLAYING, an editor owns everything

Input lives in exactly one CONTEXT at a time, and there are three shapes of it:

| Context id | When | What is live |
|---|---|---|
| `gameplay/war` | playing, War posture | movement, traversal, combat, all 24 spell slots, everyday verbs |
| `gameplay/peace` | playing, Peace posture | everything except anything that reaches the damage path |
| `editor/<Name>` | that runtime editor is open | the shared editor verbs + THAT editor's own tools, and nothing else |

**An open editor beats the posture unconditionally.** The runtime already worked this way —
`IsGameplayInputSuspended` freezes gameplay input while any editor is open — and the
configuration layer now agrees with it. `InputContexts.Current` is the one place that
answers, and it READS `GameEditorManager.ActiveEditor` rather than being told: that field is
written from six places, and pushing from all six is the shape that drifts.

- **Shared vs owned is the whole design.** Selecting, drag-selecting, zoom, scroll, undo,
  redo, save, close and delete must behave IDENTICALLY in all seventeen editors, so they are
  declared once in the `EditorShared` map with no `OwnerEditor`. Everything else is one
  editor's tool: same `Editors` bit, plus an owner, so it is live in that editor and nowhere
  else. That is why the Tile brush and the Buildings collider brush can both be `B`.
- **Two editors sharing a key is not a conflict**, and `InputContextLayerTests` pins it — the
  obvious "fix" would take away the property that each editor gets a whole keyboard.
- **Adding a tool to an editor is: an action in `Editor.<Name>`, one `Tool(...)` line in the
  catalog, one `EditorInput.Tool(map, action)` read.** Never a raw key read — a literal is
  invisible to the Controls editor and does not move when the key is rebound.
- **`OwnerEditor` is the editor's EXACT `EditorName`, spaces and all — `"Tile Editor"`, not
  `"Tile"`.** That string is what `InputContexts.Current` puts in the context id, and
  `InputContextPolicy.IsLive` compares the two; a mismatch is silent and kills every tool of
  that editor. The map name is a separate SLUG (`Editor.Tile`) and deliberately does NOT
  match, so comparing the two proves nothing. It shipped wrong on the first pass — all 35
  tools dead — and the fixture meant to cover it PASSED, because it derived its editor list
  from those same owners and so compared one half against itself. `EditorReachabilityTests`
  reads the other side: the `EditorName` declarations in production source.
- **Every editor must have a General Editor entry, and that is now load-bearing.** With the
  F-keys retired, an editor missing from the ESC list cannot be opened at all — and nothing
  throws to say so. `EditorReachabilityTests.EveryEditor_HasAGeneralEditorEntry` walks the
  shipped `EditorName` declarations, not a list anybody maintains by hand.

Rules that follow:

- **Never pair an action with a hardcoded `KeyCode` for the fallback.** That literal does not
  move when the action is rebound, so an override applies HALF of itself, silently, and only
  under the 2022.3 event-drop bug the fallback exists for. Measured before the fix: moving
  `darkball` from `1` to `5` left `1` still casting it. Use `InputBindingResolver`.
- **A binding built in C# is a binding no audit can see**, and it is invisible three ways at
  once: no scan over the asset finds it, the Controls editor cannot list or move it, and the
  conflict scanner cannot check it. Every duplicate-key bug this project has shipped was one.
  There were FOUR systems doing it — `PauseMenuUI` built seven (including a straight duplicate
  of `Gameplay/Pause` on `p`), `TileEditorInputHandler` eight (one of them Redo on
  `<Keyboard>/z`, the same path as Undo), `PickupSystem` an Interact on `e` the asset already
  had, and `InventoryUI` one on `tab` that belonged to the stance toggle. All gone.
  `BindingConstructionGuardTests` now refuses BOTH `new InputAction(` and a
  `"<Keyboard>/…"` path literal outside `Core/Input`.
- **Two guards, two axes, and the older one only covered half.**
  `InputCentralizationGuardTests` forbids reading a DEVICE (`Mouse.current`,
  `Keyboard.current`); `BindingConstructionGuardTests` forbids DECLARING a binding outside the
  asset. The first was green for the life of the project while all four systems above declared
  bindings in code — it was never asking that question.
- **Binding overrides are keyed by binding ID, so duplicate ids are a live defect.** The shipped
  asset had two pairs sharing one (`Inventory`/`MiddleClick`, `SpellTeleport`/`ToggleStance`)
  plus a duplicate ACTION id (`SpellBoomerang`/`ToggleStance`): a rebind of either would have
  moved both. `InputServiceTests.AssetIds_AreUnique` pins it.
### Editors are reached from Escape, not from the F-row

**The thirteen editor toggles ship UNBOUND.** Every runtime editor is opened from the General
Editor on **Escape**. The F-row is free except F1, which cycles the debug HUD's levels (it is an
overlay you flip while PLAYING, not an editor — see "The debug HUD" below).

That retired every same-map collision the project had: F2 held Combat Ranges AND Time &
Weather, F3 held Spawner AND Lighting, F5 held Entities AND QuickSave, F9 held Debug HUD AND
QuickLoad — and while a perf-probe overlay was up, F2–F7 also fired the probe's bisection.
Thirteen keys carrying twenty meanings, three pairs separated only by a modifier that lived in
C# rather than in the binding.

- **The actions are NOT deleted, and they ship with an EMPTY BINDING rather than with none.**
  That second half is load-bearing and shipped wrong: `ApplyBindingOverride` writes into a
  binding SLOT and cannot create one, so fourteen actions with zero bindings were listed by the
  Controls editor, could be clicked, and answered "no tiene ningun binding que reasignar" — the
  panel refused the one thing this note promises. An empty-path binding is the InputSystem's own
  "unbound": `effectivePath` is empty, the action resolves no controls, and an override moves it
  onto a real key and persists by binding id like any other. Verified in both directions:
  overriding an empty binding onto `<Keyboard>/f8` resolves 1 control, and overriding a bound
  one to `""` resolves 0. `EditorEntryPointTests.EveryEditorToggle_ShipsUnboundButAssignable`
  pins exactly one empty slot each.
- **`EditorHotkeyBindings` no longer carries a `Hotkey → KeyCode` table.** It did, feeding
  `UnityEngine.Input` directly, which meant clearing a binding cleared none of the key — the
  legacy leg of the OR-gate went on answering for F1–F12 forever. Both halves come from the
  live binding now, which is what made retiring the keys a data change rather than a code
  deletion. Its `FallbackPath` (EditMode only, when `InputService` is absent) mirrors the
  asset and answers null for the retired toggles, so the suite cannot disagree with the game
  about which keys exist.
- **Still bound, because they are not editors:** Escape (General Editor — the only way in),
  backquote (DevConsole), Ctrl+F5 / Ctrl+F9 (quick save / load), leftCtrl / leftAlt (the
  modifier probes), and F1 (`Editors/ToggleDebugHUD`, the debug HUD's level cycle). F1 and not
  F3: F2-F8 belong to the Tile and Buildings perf probes while their overlay is up.
  `EditorEntryPointTests.TheDebugHud_CyclesOnF1_AndF1MeansNothingElse` pins it.
- `EditorEntryPointTests` pins all of it, including the half that makes it safe: every retired
  toggle has a General Editor entry. An editor with no hotkey AND no menu entry is one nothing
  can open, and it would fail silently — nothing throws when a key never fires.

The **only legitimate exceptions** to the rule are:

- The four core helpers themselves (they obviously read from both backends).
- Diagnostic / boot-race null-checks (`if (Mouse.current == null) ...`).
- `mouse.delta.ReadValue()` for raw mouse-delta which `MouseInputManager` doesn't expose yet — flag as a TODO if you find a third callsite. Scroll wheel IS centralized: use `MouseInputManager.GetMouseWheelDelta()`.

If you need a key the existing helpers don't expose (e.g. `KeyboardInputManager.WasF2PressedThisFrame()` for F2-rename), add the helper rather than a new direct read.

### War / Peace stance

`Valkur.Core.PlayerStance` decides whether a combat binding does anything at all. **Peace is a
SAFE POSTURE, not a second key layout** — combat is unavailable, not remapped. It exists because
nothing in the damage path reads a faction (`Projectile` and `MeleeCombat` contain no such check,
`EntitySetup` gives every NPC a `Health`), and left click both locks a target AND casts the
primary spell, so clicking a vendor to talk to her threw a fireball at her and she could be killed
by trying to trade. The freed-up keyboard is the consequence, not the reason.

- **Consulted at the READER, never at the action map.** `InputBlocker`'s own comment records why:
  half the project reads through `MouseInputManager` / `KeyboardInputManager`, which OR the legacy
  backend, so `Map.Disable` silences bound actions and leaves every helper-polling callsite
  untouched. A map-based stance leaks exactly there, silently.
- **One gate, one place.** The entire war surface — LMB/RMB/MMB and the 24 spell hotkeys — lives
  in `PollCombatActions`. `MeleeCombat` reads no input at all.
- **The whitelist is enforced at ASSIGNMENT and again at READ, and the two are not redundant.**
  `InputContextPolicy.Evaluate` refuses to give a Peace binding to anything whose descriptor
  says `ReachesDamage`; `IsLive` refuses it again at the reader, so a `controls.json` written
  by an older build — or by hand — cannot re-open the hole. That is what makes Peace a property
  rather than a convention: a guarantee the player can configure their way out of is not one.
- **An action may be SILENCED (mask `None`), and five may not.** Silencing is what makes "turn
  off one spell without leaving War" reachable, and it is only safe because the Controls editor
  lists an action that BELONGS to the context (`InputContextPolicy.BelongsTo`) whether or not it
  is live — a row that vanished when you switched it off would be a switch with no way back.
  The exceptions are declared, not inferred: `InputActionDescriptor.ContextLocked` is true for
  Move, Look, Dash, ToggleStance and `Editors/OpenGeneralEditor`, each of which is the only way
  out of its own mode, and for every non-rebindable action (a path nobody may move is not a
  preference in the other axis either). Damage actions stay ABSENT from the Peace list rather
  than silenced-looking, which is the older decision and still the right one.
- **A spell SLOT is the unit of trust, not the spell.** All 24 slots are marked as reaching
  damage, healing and warding ones included, because the executor dispatch is shared and a
  spell's type is data — a slot whitelisted for Peace today becomes a damage slot the moment
  its `SpellDefinition` is retuned.
- **Peace is now a real LAYOUT, not one early return.** Each action carries a stance mask the
  player can narrow or widen in the Controls editor, and `PollCombatActions` checks the
  per-slot mask on top of the coarse gate — so a player can silence one spell without leaving
  War, and can give a mouse button a harmless verb in Peace. The coarse gate stays as the fast
  path and as the thing `StanceGateTests` pins the ORDER of.
  **Both halves of that sentence were unreachable until the mask surface was fixed**, and the
  reasons are different. Silencing one spell needed mask `None`, which `Evaluate` refused
  outright; giving a mouse button a Peace verb needed the editor to be able to bind a mouse
  button at all, and its capture poll read only the keyboard. A chip is now drawn only where
  something reads the mask, and it is drawn LOCKED rather than as a toggle that refuses — the
  shipped panel offered eight interactive chips of which six had no reader, so it reported
  changes it could not make. `Gameplay/Pause` and `Gameplay/DropItem` gained the readers they
  never had (`PauseHotkeyReader`, and one line in `InventoryUI`); `Pause` had been bound to `p`
  for the life of the asset with nothing reading it at all.
- **The dash is NOT combat** and was extracted into `PollTraversal`, which runs on both sides of
  the gate. Nothing auto-switches, so a Peace stance that also removed the dash would have no
  recovery from being jumped.
- **Tab is the control; the HUD chip is the indicator.** Tab is safe against uGUI's legacy
  `StandaloneInputModule` because only a focused `TMP_InputField` consumes it, and a focused field
  means chat or the console is up — which is when `InputBlocker` is set and `KeyboardInputManager`
  refuses every key but Escape / backquote / Enter. Impossible by construction, not avoided by care.
- **The Spells Editor's redirected click is deliberately NOT gated.** It is reached from
  `PollRedirectedPrimaryCast` inside the editor-suspended branch, above the gate, so it never
  arrives. That is by construction rather than by intent — `StanceGateTests` pins it, because the
  obvious tidy-up is hoisting the check to the top of `Update`.
- Defaults to **War**, so nothing behaves differently until the player asks.

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
| `Bootstrap/` | Game/EntitySetup, DevConsole, scene composition, the boot sequence (`GameplaySceneSetup.Sequence.cs`) |
| `Chat/` | In-game chat |
| `Combat/Resources/` | Health, Mana, Experience |
| `Combat/Damage/` | FloatingDamageNumber/Spawner, GrayscaleDeath, DeathDropSystem |
| `Combat/Mechanics/` | MeleeCombat, DashAbility, MouseTargetDetector, ComboCounter, NPCRespawnSystem |
| `Combat/Feedback/` | CastOutline, CombatFeedback, CombatAudioSystem, ToastSystem, ExplosionEffect, CombatRangeVisualizer |
| `Combat/WorldUI/` | WorldHealthBar, WorldManaBar, WorldDashBar, FacingIndicator |
| `Combat/Lifecycle/` | TimedDespawn, SpawnStabilizer |
| `Combat/Death/` | DeathSequenceController (+ .Rescue), PlayerSpiritState/Visuals, SpiritWorldGrayscale, SpiritAltarPathHighlighter, PlayerCorpseMarker, PlayerDeathDropSystem, ResurrectionAltarRegistry, DeathLitter, DeathStateSave |
| `Combat/StatusEffects/` | Status effect implementations (Burn, Poison, Stun, Freeze, Slow) |
| `Editors/_Shared/` | EditorCameraPanController, EditorUIHelpers (cross-editor) |
| `Editors/_Shared/Workspace/` | `EditorWorkspaceService` — the single owner of editor layout/session/selection persistence |
| `Editors/Camera/` | Camera Editor — no hotkey, opened from the General Editor; partials + UIBuilder + UIHoverHelp |
| `Editors/Death/` | Death Editor — no hotkey, opened from the General Editor (ESC → Muerte); partials + Tabs + Workspace |
| `Editors/Buildings/` | Buildings runtime editor — partials + UIBuilder + Outline + PerfProbe |
| `Editors/Entities/` | Entities runtime editor — partials + UIBuilder + Outline |
| `Editors/FSM/` | FSM runtime editor — partials + UIBuilder |
| `Editors/Inventory/` | Inventory runtime editor — partials + UIBuilder |
| `Editors/Items/` | Items runtime editor — partials + UIBuilder |
| `Editors/Lighting/` | Lighting runtime editor — partials |
| `Editors/Map/` | Map runtime editor |
| `Editors/Particles/` | Particles runtime editor — partials + UIBuilder |
| `Editors/Spells/` | Spells runtime editor — partials + UIBuilder + SpellPreviewGraphic |
| `Editors/Tile/` | Tile runtime editor |
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
| Spells | `Data/Catalogs/SpellCatalog.asset` — note it sits beside `Catalogs/`, not inside `Catalogs/Spells/`, which holds the individual `*.asset` definitions (edit via Inspector or the Spells editor in-game) |
| Buildings | `Data/Catalogs/Buildings/BuildingCatalog.asset` (edit via the Buildings editor in-game) — 1176 templates over 1174 sprites (two sprites carry a second template that differs in a field no instance can override: `curse_house_topdown` with/without its doorway, `totem_forest` solid/non-solid); every prop imported through the sheet pipeline is described by a `tools/atlas/generated/building_props_manifest*.json`, one per wave |
| Particles | `Data/Catalogs/Particles/ParticlePresetCatalog.asset` (edit via the Particles editor) |
| Spawners | `Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset` (edit via the Spawners editor) |
| Camera feel (shake, kick, lead, smooth follow) | `Resources/CameraFeelProfile.asset` |
| Death, spirit form, altars, rescue, corpse, death cost | **`Resources/Death/DeathTuning.asset`** — under `Resources/` because every reader (`DeathSequenceController`, `SpiritAltarPathHighlighter`, `SpiritWorldGrayscale`, `PlayerDeathDropSystem`, `XpLossOnDeathSystem`) is `AddComponent`-ed onto a bare GameObject by `GameplaySceneSetup` and has no inspector slot. Edited from ESC → Muerte |
| Lighting Presets | `Data/LightPresetCatalog.asset` (edit via the Lighting editor) |
| Chat Personas / Assignments | `Data/ChatPersonas/*.asset` (runtime half) + `Data/ChatPersonas/Profiles/*_profile.asset` (narrative half) + **`Resources/Chat/ChatAssignmentCatalog.asset`** — under `Resources/` because `ChatSystem` is `AddComponent`-ed onto a bare GameObject and has no inspector slot to be wired from. All of it is generated by `Valkur > Chat > Import Personas` from `tools/chat/generated/chat_personas_manifest.json`; the join to entities is `Valkur > Chat > Wire Entities To Personas` |
| Vendors | `Data/Vendor/Configs/*.asset` (5, generated by `Wire Entities To Personas`, seeded from `ItemDefinition.itemType`) + `Data/Vendor/EconomyGroups/EG_*.asset` (5, generated by `Valkur > Economy > Seed Economy Groups`, which also repairs the `economyGroup` reference on every run). `VendorEconomyService` stays null-safe without a group, which is exactly why all five shipped without one for months |
| Economic cycle (seed + day) | `Application.persistentDataPath/Saves/…` — the save's METADATA bag, keys `market.seed` / `market.day` / `market.seed_source`, written by `GameStateCollector` and read by `GameStateRestorer`. Not a typed field: it is world state, it is two ints, and the bag is already the project's answer for run-level facts (`run_id`, `run_ordinal`) |
| Players | `Data/Catalogs/Players/*.asset` |
| Player stats, talents, grimoire, curves | **`Resources/Progression/ProgressionCatalog.asset`** — under `Resources/` because `PlayerProgression` is `AddComponent`-ed onto the player and has no inspector slot to be wired from. It points at `Data/Progression/{XpCurve,LevelStatCurve}.asset`, five `SkillTrees/<class>/` and nine `SpellTrees/<school>/`, all generated by `Valkur > Progression > Seed Progression Content` |
| World state (placed buildings, lights, spawners, particles, tile overlays) | `StreamingAssets/{Buildings,Lights,Spawners,Particles,Maps}/*.json` (written by the Particles, Spawners, Tile, Buildings, Map and Lighting editors). `Particles/particles_instances.json` is schema v4: each record carries its own `config` (the copy of the preset it was placed with, defaults omitted), and may carry the legacy `spawn_scale_x` / `spawn_scale_y` / `reach` size ratios from v3 |
| FSM (states, assignments, animation map) | `StreamingAssets/FSM/*.json` (written by the FSM editor) — four sets: `Monster_Default` (melee), `Monster_Caster`, `Monster_Boss` (no `FleeState`), `NPC_Passive` (Idle/Unconscious/Death only). All 19 monsters are assigned in `assignments.json`; the resolution order is `by_eid` → `by_archetype` → `MonsterDefinition.fsmSet` → hard-coded IdleState |
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
| `buildings-editor` | Anything involving the Buildings Editor (window or runtime) |
| `tile-editor` | Anything involving the Tile Editor |
| `particles-editor` | Particle presets, `ParticleEmitter`, VFX beauty work, Particles Editor |
| `spell-vfx-director` | Spell look & game-feel — slash/projectile/area silhouettes, timing, impact, hit-stop, camera shake |
| `editor-ux-parity` | Audit / enforce UI/UX parity across in-game runtime editors — chrome, gestures, workspace persistence, theme, feedback |
| `editor-workspace-architect` | The editor workspace persistence LAYER itself (`_Shared/Workspace/`, `DraggablePanel` state, the `GameEditorManager` hook, the store, the contract test). Never edits the seventeen editors |
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
- **`Sprite.Create` defaults to `SpriteMeshType.Tight`, and on an atlas page that is a
  4 459x cost.** Tight traces the ALPHA OUTLINE of the rect to build a fitted mesh, and the
  price scales with the region, not with the sprite. `BuildingObject.Assembly` slices every
  building into a footprint half and a canopy half — two `Sprite.Create` calls per instance,
  each carving a ~1024 px square out of a **4096x4096** atlas page. Measured on the shipped
  art: **20.15 ms per call against 0.005 ms for `FullRect`**. Times 602 calls that was
  **6 953 ms of an 11 286 ms boot — 60 % of the whole arranque was Unity outlining building
  sprites**, and the mesh was read by nothing: these renderers draw near-opaque pixel art and
  the buildings subsystem contains no `PolygonCollider2D`, because collision is the painted
  per-cell grid. After: the step is **324 ms** and the boot **5 760 ms**, with 301 buildings
  on screen and no visual change (a tight mesh and a full quad show the same pixels; alpha
  lives in the shader).
  **How it was found is the reusable part, and it is three measurements, not a guess.**
  Per-STEP timing said one step was 60 %. Per-SUB-STAGE timing said it was the spawn pass and
  not the collider pass (137 ms) — which killed the obvious suspicion. Re-running the same
  load with everything warm (5.8 s against 6.8 s cold) said it was not asset loading either,
  which left only the work itself; and 22 ms for what is nominally an object allocation is
  not "instantiation is slow", it is work nobody asked for. Only then was the code read.
  Every other runtime `Sprite.Create` in the project draws from a 2x2 or 4x4 texture, where
  Tight is free — the trap needs a BIG source region to bite, so grep for the ones that slice
  an atlas.
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
- **The aim indicator has ONE job, and the interesting part is everything it was stopped from
  doing.** `FacingIndicator` (`Combat/WorldUI/`) answers WHERE THE PLAYER IS POINTING and is not
  allowed to answer anything else. It replaced a floating white chevron that sat on `Entities`
  at `Z_SKY + 10` — the same Z-depth-as-order bug as `LightningBoltFX`, so nine sorting layers
  drew over it — and pulsed its alpha with `sin(t*3)` forever, a lamp with a flicker. It now
  lies on the floor: an unparented root that follows the feet, ONE `GroundPlane` squash with the
  aim rotation as its CHILD, and TWO additive layers — a hard chevron and the glow around it.
  **It had four, and the two that went are worth knowing about.** A light pool at the feet and a
  60-degree wedge sweeping the ground between the pool and the tip read on screen as a cone of
  particles growing out of the character's boots, and were cut for exactly that. The pool's JOB
  survives the pool: without something joining marker to body, the tip is an arrowhead floating
  a unit away with nothing connecting the two (measured on the first live capture), and the aura
  carries that now by being large enough to belong to the chevron rather than to sit beside it.
  The aura is generated from the SAME two segments as the tip with a wide falloff, so the glow
  is chevron-SHAPED — a round halo behind an arrow reads as two objects that happen to overlap.
  A dark ALPHA rim went with them, and that is a real trade rather than a tidy-up: it was the
  only reason a white chevron kept a silhouette over pale stone, and a white aura cannot do that
  job because white saturates all three channels at once while a dark additive pixel adds
  nothing. On dark ground the glow reads better than the rim ever did; on a pale cobbled street
  the chevron is softer than it was.
  Test trap the redesign exposed: the aura's alpha MASS leans to -X (measured 0.523 against
  0.479) because the two arms run back to the tails while the apex is a single point, so a
  mass-comparison test FAILS the correct implementation. What separates a chevron glow from a
  disc is that it follows the ARMS rather than the radius — sample two points equidistant from
  the centre, one on an arm and one in the notch between the tails (measured 1.000 against
  0.055).
  **Three second jobs were built here and then deleted, and each was individually defensible**:
  a sweep that dimmed while the primary recovered, a tip that contracted during a cast wind-up,
  and a ring instead of a point in the Peace stance. Together they made the one shape under the
  character mean four things at once, so no glance could separate "my spell is recovering" from
  "I am unarmed" from "I turned". A fourth was rejected before it was written — leaning the tip
  toward `MouseTargetDetector`'s target makes the rig point where the player is not.
  `FacingIndicatorRigTests.Source_HasOneJob_*` reads the production source and fails on
  `PlayerStance`, `SpellCaster`, `CastPhase`, `Cooldown`, `ElementPalette` or
  `MouseTargetDetector`, because every one of those arrives looking small.
  **The PULSE is the one thing that moves besides the heading, and the line it sits on is the
  design.** A STATE has to be READ — a dimmed sweep meaning "your spell is recovering" is a
  second readout competing with the first. An EVENT tells the player nothing they did not just
  do: a flash on the frame they cast, and one on every blow of a pick or an axe, is
  confirmation at the place they are already looking. The test is whether anyone would ever
  have to LOOK at the rig to learn something. It is PUSHED IN — `FacingIndicator.Pulse()` is
  called by whoever acted — so the rig still has no idea what a spell or a tree is, which is
  what keeps the source guard green and is why the guard is the proof rather than a comment.
  Both callers are single-fire and finding those points was the work: the cast pulse is gated
  on `sameCastStillPlaying`, the flag `TriggerCastAnimation` already computes, because a
  channelled beam re-enters that method EVERY FRAME while held and an ungated pulse holds the
  rig lit for the whole beam instead of marking its start; the harvest pulse sits in
  `PlayerController.PlayWorkSwing`, which `HarvestNode.LandBlow` calls once per blow and which
  is the only harvest seam that is player-side by construction (`BlowLanded` fires on the NODE
  and its `HarvestBlow` carries no attacker). One pulse for every action, deliberately: giving
  a cast and a pick strike different looks would make the pulse a readout of WHICH action.
  A pulse also repaints only while it is live — at rest the four colours are exactly what the
  build painted and nothing writes them, because a `SpriteRenderer.color` set dirties URP's
  batcher. Deleting readiness
  also retired the `PlayerController.PrimaryCastKey` accessor that had been added for it — a
  reader-less accessor is the authored-and-inert shape this file already records a dozen times.
  Its RADIUS IS FIXED for the same reason: a size that tracked the primary's `range` would
  resize on every spell swap and read as a glitch.
  **It is WHITE, one shade per layer** (warm cream at the ground, near-neutral field, faintly
  cool tip), and it used not to be: it took the primary spell's palette through the flourish's
  `ResolveSwatch` line, so a fireball drew a red marker and the aim indicator became a readout
  of a loadout choice. Three things follow. The gains are HALF what the coloured rig used, which
  is arithmetic and not taste — the fireball red carries luminance 0.49 per unit of colour
  against white's 1.00. The dark outline matters MORE, because a red glow blows out one channel
  and leaves the ground texture legible through the other two while white saturates all three at
  once. And because nothing can change a colour any more, the four are painted ONCE at build
  (`PaintLayers`) rather than every frame — `TheLook_IsConstant_*` pins that only the heading
  moves.
  **Depth is the body's own sorting layer**, rebased on the feet every frame with `YSortEntity`'s
  formula (COMPUTED, not read back — both run in `LateUpdate` in an undefined order, so a read
  trails the body by a frame and the rig flickers across it while running), each aim piece
  behind or in front by the SIGN of the aim, pool always behind. It shipped on `Overhead` first,
  which made "always visible" literally true, and the first live report was the tip drawn over
  the character's legs when aiming north; a decal north of the feet is farther from the camera
  than the body standing on it, and only the body's layer can say so.
  Related test trap: a 60-degree hollow sector's MEAN alpha is ~0.05 by construction, so a test
  asserting the sprite is "mostly ink" fails a correct sprite — assert presence and side.
- **A ring at the mouse pointer, and the OS cursor deliberately left alone.**
  `CursorImpactFX` (`Combat/Feedback/`) opens a white ring at the pointer when the player casts
  or lands a harvest blow. The alternative — hiding the OS pointer and drawing our own
  crosshair, which is the only way a cursor can genuinely recoil — was rejected on its cost,
  not its look: a frame of lag on the one thing the player aims with, stutter whenever the
  framerate dips, handing the pointer back in seventeen runtime editors plus the inventory's
  drag-and-drop, the chat and every menu, and it would put the documented InputSystem
  mouse-freeze bug ON SCREEN, where today the OS pointer is ground truth and always right. A
  transient effect has none of that: nobody can tell that a ring living a fifth of a second
  started sixteen milliseconds late. It FOLLOWS the pointer for its whole life — anchored, it
  reads as a mark on the ground you clicked rather than as the cursor reacting.
  Three things about the canvas are load-bearing and all three are ABSENCES. No
  `GraphicRaycaster`, and `raycastTarget = false` on both images: a full-screen click-eater
  over every panel in the game would break the very action that spawned the ring, and only
  while it was on screen. No `CanvasScaler`, because a cursor accent should be the size the OS
  pointer is and not grow with the render resolution — and its absence is also what makes one
  canvas unit one screen pixel, so the pointer position goes straight into `position`. And at
  rest nothing is written at all, not even the position.
  **Measuring it needs two tricks.** Its own `LateUpdate` overwrites any hand-set position on
  the next frame, so a probe must disable the component before placing it (and restore that in
  a `finally`). And in the Editor the pointer is routinely OUTSIDE the game view — measured at
  (3326, 115) against a 1600x800 view — so the ring renders off-screen and a capture looks
  empty for a reason that has nothing to do with the effect. What settles "is it drawing" is a
  signed diff of two captures: measured, `min = 0.00` and `max = +169`, i.e. it only ever adds
  light. That mattered because the first capture looked like it had a DARK core, which was a
  round cobblestone in the tile art showing through.
- **`SpellCastAnchor.Hands` is 72 % of the way up the body, and for a SWING that is the
  head.** `ProjectileExecutor.ResolveCastOrigin` places a cast at the sprite's visual centre
  plus `0.45 x half-height`, which is right for a conjuring — a fireball should leave the hands
  — and wrong for a slash, which is swung from the trunk. Measured live on the shipped dwarf: a
  1.86-unit body with Hands at 72 % and Center at 50 %, a drop of **0.42 u**. All thirteen
  slashes shipped on Hands, twelve of them by omission (the field postdates the assets, and a
  missing key deserialises as 0), so the blade appeared to grow out of the character's head.
  The fix is the `castAnchor` field the data model already had, set to `Center` on all
  thirteen — and it is DATA rather than an `if (type == Slash)` in the resolver on purpose,
  because a hard-coded rule beside an authored field makes the field unfalsifiable: the Spells
  Editor would show an Anchor dropdown that could not move the whole slash family, which is
  `VortexFieldExecutor`'s `spawnAtMouse || isPull` bug wearing a different hat. What data
  cannot do alone is survive the NEXT slash, which is created with the enum's default;
  `SlashCastAnchorTests` walks the shipped catalogue for that. Both slash paths are safe to
  change from one field because `SlashExecutor` resolves the origin ONCE and hands the same
  point to `RegularSlashAttack.Spawn` and `SlashAttack.Spawn`, each of which does
  `transform.position = origin` and derives every later sweep from it — so the drawn arc and
  the damaged arc cannot separate.
  **Two traps when verifying a data edit like this.** `refresh_unity(scope="scripts")` does not
  reimport an edited `.asset` and `AssetDatabase.LoadAssetAtPath` returns Unity's IN-MEMORY
  copy, so an EditMode test over shipped data measures the stale object and passes or fails for
  the wrong reason — use `scope="all"` and assert the loaded value against the file's own text
  in the same probe (measured here: 13 memory, 13 disk, 0 disagreements). And a slash cannot be
  caught by a screenshot taken in the frame it is cast: `slash` authors `prepareDuration: 0.06`,
  so the executor runs several frames later and the capture finds nothing. Drop
  `Time.timeScale` to ~0.04, cast, capture on the next call, and restore it.
- **`SpellCastAnchor` is a vertical enum, so it cannot say where a DRAGON's mouth is.** It
  is a signed fraction of the caster's half-HEIGHT (Feet -1, Center 0, Hands 0.45, Head 1)
  plus a clearance along the aim — exactly right for a humanoid, whose sprite is about as
  wide as it is deep and whose hands are above its feet. `red_dragon` is **8.23 x 4.55 world
  units** and its mouth is three and a half units in FRONT of its pivot, so the error is
  HORIZONTAL and every value of the enum is equally wrong: measured, its breath was born at
  (+0.50, 3.29) against a mouth at (±3.5, 2.4), i.e. out of the middle of its own back.
  `EntityAssetConfig.castMuzzle` + `castMuzzleFrames` and the `CastMuzzle` component are the
  answer, consulted by `ProjectileExecutor.ResolveCastOrigin`.
  - **The muzzle belongs to the CREATURE, never to the spell** — the same argument
    `CastVariant.spellKeys` makes. A `SpellDefinition` is shared by everything that casts it
    and knows nothing about any of their art, so `castForwardOffset: 3.5` on `flame_breath`
    would put every other caster's fire three and a half units out in the open. It is also
    the wrong AXIS: that offset runs along the aim, and this art exists only facing east and
    west, so a dragon aiming north would breathe from a point above its own spine.
  - **It claims Hands and Head only.** `Feet` and `Center` are EXPLICIT requests for the
    body — `thunderclap` authors Center because it is a clap AROUND the caster — so a muzzle
    moving them would put the ring three units in front of the dragon and out of the fight.
    Every already-authored intent survives untouched, which is why no existing asset changed.
  - **Measured PER FRAME, because the head sweeps.** A single pair was tried first and is
    kept as the fallback: across the dragon's eight cast frames it lands on the mouth in four
    and in open air in the other four, because rearing moves the mouth from (2.82, 1.31) to
    (4.09, 3.98) — 1.3 units forward and 2.7 up. Baked by
    `Valkur > Monsters > Bake Cast Muzzles`, which is **opt-in by authoring `castMuzzle`**:
    the measurement is "the leading edge of the silhouette at the head's own height", which
    is the head on anything drawn in profile and is a RAISED AXE on the barbarian.
  - **The stored X is forward-POSITIVE and the sign comes from which half is DRAWN, read off
    the frame's own name.** A direction table would be right for one pipeline and silently
    wrong for the other: measured on shipped data, the player pipeline puts S, SE, E, NE and
    N on the east half while the wave13 monster pipeline puts only SE, E and NE there and
    gives S and N to the WEST half.
  - **Fractions of the frame's own bounds, never world distances.** Each frame is trimmed to
    its own alpha, so the sheets differ in width (the dragon's idle frames are 638 px and its
    cast frames 527) and a fixed offset would be right for one state and wrong for the other.
    A fraction also survives `scaleConfig.scaleIdle`, since `SpriteRenderer.bounds` is world
    space and already carries the transform scale.
  - **The silhouette heuristic has two failure modes and both are real on this one monster.**
    Taking the leading window's raw vertical span merges body parts — on `idle_e2` the
    leading 45 px holds the head at rows 3-56 AND a hind leg at row 274, so the midpoint
    lands in the empty air between them; following the ink back from the tip with a
    continuity tolerance fixes it. And on `cast_e6` the FORELEG reaches further forward than
    the snout, which no leading-edge rule can survive. `CastMuzzleFrame.handTuned` is the
    escape hatch: the baker re-measures everything and carries those rows across, the same
    split the crafting importer uses. **Verify a bake by eye, frame by frame** — 2 of the
    dragon's 30 unique frames were wrong and both were invisible in the numbers.
  - **`MeleeCombat` is deliberately NOT routed through it.** It origins the drawn arc AND the
    damage circle at `transform.position` with one radius, on purpose; moving the arc to the
    mouth would separate the two, which is the defect its own comment records having fixed.
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
- **Buildings save position-collapse bug** — root cause unknown but mitigated by 3 guards in `BuildingsRuntimeEditor.Persistence.cs`. If the Buildings save ever logs `ABORTING save — ...`, that's this bug firing. Read `.github/incidents/BUILDINGS_SAVE_POSITION_COLLAPSE.md` for the recovery procedure and the next-step investigation checklist.
- **Never `Undo.RecordObject` in a bulk asset-import tool.** `BuildingPropImporter` created 193
  `BuildingTemplateData` assets and recorded each one for undo. They landed on the *global*
  editor undo stack, and the first thing that popped it — the EditMode suite, which exercises
  the runtime editors' undo — reverted all 193 IN MEMORY to their empty creation state while
  the correct data sat on disk. They then stayed dirty-and-empty, so the next `SaveAssets`
  would have written the emptiness over the good data. Symptom: `assetPath` reads `''` from
  `AssetDatabase.LoadAssetAtPath` while `File.ReadAllText` on the same `.asset` shows the real
  value. Use `EditorUtility.SetDirty` alone for data an operator re-runs rather than undoes.
  `BuildingTemplateOriginalScaleBackfill` still carries the same latent hazard.
- **A script can exist, compile, match its own guid, and STILL not be bound to its class —
  and every asset of that type then loads as null, silently.** Measured on `SkillTree.cs`:
  the file was present, its `.meta` guid matched what the assets referenced, it declared one
  top-level `public sealed class SkillTree : ScriptableObject`, it was the only type of that
  name in the domain, and `System.Type.GetType("Valkur.Data.SkillTree, Valkur.Data")` resolved
  — while `MonoScript.GetClass()` for that same file returned **NULL**. Consequences, all
  silent: `FindAssets("t:SkillTree")` returned 0, `LoadAllAssetsAtPath` returned one object and
  that object was null, all five `*_skill_tree.asset` loaded as null while their 35 `SkillNode`
  siblings and all 80 spell-tree assets were fine, and `ProgressionCatalog.skillTrees` held five
  entries and five nulls. Every playable class had lost its talents with nothing logged and
  perfect YAML on disk (`classKey: dwarf`, seven nodes). The tell that separates it from the
  deleted-script case is that `AssetDatabase.GUIDToAssetPath` SUCCEEDS —
  `ResourcesScriptIntegrityTests` checks exactly that and therefore cannot see this;
  `ShippedScriptBindingTests` asks the next question, `GetClass() != null`, over the 51 distinct
  script guids the 5,783 shipped assets reference.
  **ORDER MATTERS IN THE FIX AND THE OBVIOUS ORDER FAILS.** Force-reimport the `.cs` FIRST — that
  rebuilds the file-to-class binding — and only then the `.asset` files, which reconstructs the
  cached nulls. Reimporting the assets first changed nothing at all (measured: `nullLoads` stayed
  5/5), because the type still could not be constructed; reimporting only the `.cs` fixed
  `GetClass()` and `FindAssets` (0 → 5) and left the already-loaded nulls in memory. Both halves,
  in that order. And never `ForceReserializeAssets`, which flushes the bad memory state onto the
  good file — this state is one `AssetDatabase.SaveAssets()` away from becoming the
  216-deleted-building-templates incident, and `SaveAssets` writes everything dirty, not just
  what you edited.
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
  NEXT placement and none of the existing ones. Before this, every row of the Particles editor's
  properties panel edited the shared asset and an author tuning the emitter they had just clicked changed
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
- **`DirectionalAnimator.SetState` renders nothing in Edit Mode, and it fails as a plausible
  number rather than as an exception.** It really does apply its frame immediately — it calls
  `AdvanceFrame` on a state change on purpose, so a new state is visible without
  frame-interval lag — but the animator caches its `SpriteRenderer` in `Awake`, which Unity
  never calls on a component added in Edit Mode. So the write lands on null and the renderer
  keeps whatever `EntityAnimationBinder` seeded: the idle set's FIRST bucket, which is South,
  which on the monster pipeline is drawn from the WEST half. Measured in `CastMuzzleTests`,
  a caster faced EAST reported its muzzle at **-4.67** — exactly `idle_w0`'s 0.938 times the
  idle frame's 4.984 half-width, a real number about the wrong frame. An EditMode fixture
  that needs a specific frame must set `SpriteRenderer.sprite` itself and say so.
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
- **A number can be real, internally consistent, and about something other than what you
  asked.** Three sightings in one day, three different mechanisms, one shape — which is why
  this is a named pattern here rather than three anecdotes. `Time.deltaTime` read **0.0016**
  under `execute_code` because nothing was rendering, so three rounds of vortex force tuning
  measured the harness and each "fix" was a correction to it. A test job reported
  **`completed 13281` against `total 7148`**, which is structurally impossible for one pass
  and was two concurrent runners crossing each other's log windows — twelve red tests that
  were all `Unhandled log message` from OTHER fixtures, not one assertion about the code under
  test. And a spawner round trip saved perfectly and loaded 150 tiles away for months because
  each half was internally consistent and only the composition was wrong.
  **The defence is a structural check that is independent of the value**: `completed > total`
  cannot happen in a single pass whatever the tests say, exactly as the `HEAD`-vs-working-tree
  schema diff finds fields no shipped-data test happens to cover. Reach for one of those
  BEFORE reading the failures — without it, the twelve red tests above cost twenty minutes of
  investigating TileEditor code that was fine.
- **A LOG LINE HAS A TIMESTAMP AND A POSITION, AND READING ONE FOR THE OTHER IS THE SAME TRAP.**
  Fourth sighting, 2026-09-10, and this one was a wrong CONCLUSION announced to three people.
  The MCP bridge went silent for every session at once; `Editor.log` was being written three
  seconds ago, and `tail`+`grep` over it turned up 94 WebSocket keep-alive failures. Both facts
  are true. The conclusion drawn from them — "the socket is dead, restart the bridge" — was
  wrong: those lines were HISTORICAL, and the silence was a test run owning the main loop, which
  is exactly what makes MCP For Unity stop answering. It came back on its own.
  **What separated the two hypotheses was not reading more lines, it was measuring two different
  quantities**: where the WebSocket lines sit RELATIVE TO THE END of the file (not in the last
  4000), and whether the log is GROWING with runner lines (measured by a peer at 1 MB in 6 s,
  923 of 2000 lines from the test runner). "The log was written 3 seconds ago" is true and
  answers a third question. `stat -c %s` twice a few seconds apart costs nothing and is the
  check that would have refused the wrong answer.
- **Reflection sees SYMBOLS, not FILES, so it cannot confirm a comment-only edit is loaded.**
  The rule above about confirming the new code is loaded — `typeof(X).GetMethod("NewThing")
  != null` — proves a symbol arrived and says nothing about whether the FILE Unity compiled is
  the file on disk. A change that only touches comments, whitespace or a doc block is invisible
  to it, and every type-loaded probe comes back green on a stale assembly. That is not
  hypothetical: it is what stood between a measured suite result and a merge to the default
  branch. The probe that can see it compares timestamps —
  `File.GetLastWriteTimeUtc(typeof(X).Assembly.Location) >= newest .cs under Scripts/` — and it
  is cheap enough to run beside the reflection check rather than instead of it.
- **A lost acknowledgement on a WRITE leaves you unable to know whether it happened, and no
  read-side probe on this bridge can tell you.** The MCP link drops often under load —
  `disconnected while awaiting command_result` and `TimeoutError` arrived nine times in one
  session — and retrying is correct for a READ like `get_test_job`, where a lost reply costs
  nothing. It is not correct for a WRITE. A `run_tests` whose response was lost may have
  started a Unity-side runner anyway, and retrying then starts a SECOND one in the same editor;
  the two runs' log output crosses into each other's assertion windows and the suite comes back
  red on tests that are fine. `clear_stuck` is NOT the remedy, and believing it is makes this
  worse: it reports on the MCP job registry, while the orphan lives in Unity's own TestRunner,
  so it answers "No running job to clear" with a runner live. **The job registry and the Unity
  runner are two different sources of truth.** What
  definitively clears an orphaned runner is restarting the Editor.

  **There are THREE sources of truth, not two, and the third is the one that gates
  `refresh_unity`.** Measured 2026-09-10 on a runner orphaned for 33 minutes: clearing
  `TestJobDataHolder.TestRuns` (Unity's own registry) changed nothing, and
  `TestJobManager.ClearStuckJob()` returned true and set `HasRunningJob` false — and the refresh
  was STILL refused with `tests_running`. What actually gates it is
  `MCPForUnity.Editor.Services.TestRunStatus._isRunning`, a loose static with its own
  `MarkStarted`/`MarkFinished` pair; calling `MarkFinished()` flipped `EditorStateCache` from
  `phase=running_tests` to `idle` and the refresh went through immediately. Note also that
  `EditorStateCache.ForceUpdate` re-reads that static, so it is not a stale-cache problem —
  asking the cache twice will not help. And `clear_stuck` really does clear its half: the
  threshold is 60 s, so a 33-minute job qualifies. It just is not the half that was blocking.

  **The Unity-side runner IS readable from here, and this note used to say it was not.** The
  probe is `Resources.FindObjectsOfTypeAll(` +
  `UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder)` through `execute_code`: its
  `TestRuns` list carries one entry per run with `guid`, `startTime` and `isRunning`, so
  "is a runner live, and is there more than one" is answerable BEFORE deciding whether a retry
  is safe — which is the whole decision this note exists for. Two details are load-bearing.
  Do NOT reach the holder through `ScriptableSingleton.instance`: that CREATES the asset on
  access, turning the probe into a write. And reflect its members with
  `BindingFlags.FlattenHierarchy`, without which inherited statics do not resolve and the probe
  reports a confident, wrong nothing — measured, it answered "no holder" with a run live.

  The same reasoning applies
  to every mutating MCP call, not only to tests. **A refusal is not proof the write did not
  land, either**: `run_tests` answered `no_unity_session; please retry` — an explicit "I never
  reached Unity" — and had started a runner anyway, which only surfaced because the retry came
  back `tests_running`. That second refusal is the thing that saved it; had the registry been a
  moment slower there would have been two runners crossing each other's log windows. Treat an
  error on a mutating call as UNKNOWN, not as "did not happen".

- **Gating a poll silently strands whatever that poll was HOLDING.** Three things in
  `PollCombatActions` are held rather than fired — a left-held primary beam, the middle-click
  laser, and a charging spell — and each is ended by a line inside that same method: the release
  check, the button-up branch, the charge poll. So the moment the Peace stance stops calling it,
  the ending never arrives and a player who holds the laser and hits Tab keeps channelling it, in
  a stance whose entire promise is that they cannot attack. `PlayerController.OnStanceChanged`
  cuts all three off the TRANSITION rather than beside the gate, because a poll would re-run them
  every frame of a stance meant to be doing nothing. The charge is the subtle one: `ChargingKey`
  makes the spell loop return early, so a charge left set would refuse every spell on the way back
  to War until that same key was pressed and released again.
- **A table that builds every row on open is a freeze, and the freeze scales with the
  catalog, not with any bug.** `ItemsRuntimeEditor.Activate()` measured **3,480 ms**, all of it
  in `RefreshTable`: 38 columns x 180 items = 6,840 live uGUI widgets, each a perfectly
  reasonable ~0.48 ms. There was nothing to optimise in a cell — the cost was volume, for a
  viewport that shows ~20 rows. That one method was the 3.5 s hitch on opening the Items editor AND 52 % of the
  entire EditMode suite (178 s of 343 s inside 37 tests), because every Items fixture calls
  `Activate()` before injecting its own three-item catalog. The body is VIRTUALISED now
  (`ItemsRuntimeEditor.Table.cs`): rows are placed by index at absolute positions, the content
  rect is sized to `rows x TABLE_ROW_H` by `RefreshTable`, and `UpdateVisibleRows` realises
  the viewport window plus four rows of overscan and drops the rest — 413 ms, 13 rows, and the
  whole EditMode suite went from 343 s to 126 s. Two
  constraints hold it up. The body carries NO `VerticalLayoutGroup` and NO `ContentSizeFitter`,
  on purpose: a layout group stacks whatever rows exist from the top and puts row 150 where
  row 0 belongs, and the fitter shrinks the content to the realised window and makes the rest
  unreachable — `ItemsTableVirtualizationTests` pins both, because both are the obvious thing
  to "add back". And uGUI performs no layout in Edit Mode, so a viewport can report zero
  height; the fallback is a fixed window (`TABLE_FALLBACK_VISIBLE_ROWS`), never "all rows".
  **How it was found is the part to reuse.** Four plausible hypotheses were each disproved by
  one measurement before the real one surfaced: "editor fixtures are cheap" (two cheap ones
  had been sampled; `Editors` was 60 % of the suite), "`BuildGrid`/`Resources.LoadAll` in
  `[SetUp]`" (0.9 ms and 3.2 ms — Unity caches `Resources`), "a fixed ~8.5 s per run"
  (`Buildings`: 372 tests in 1.5 s), and "quadratic layout rebuilds" (1.2x, 55 ms). What
  worked was bisecting the suite by NAMESPACE with `run_tests(test_names=[prefix])` and reading
  only `durationSeconds` — the per-test detail payload for 7,221 tests does not survive the
  bridge — until one group stood out, then timing `Activate()`'s callees with a Stopwatch
  through `execute_code`. Measure the group, then the method, then the callee; never the guess.
  **The whole suite was then mapped this way (2026-09-04), and the map is the reference for
  "is this group slow": every group sits at 4-25 ms/test — `Buildings` 4, `World` 8, `Input`/
  `VFX`/`AI`/`Save`/`Terrain`/`Core` 8, `TileEditor` 12, `Data`/`Player`/`Spells` 13, `Chat`/
  `Combat`/`UI` 15, `MapEditor` 17, `Particles` 24 — and anything above ~100 ms/test was one
  of five things: the two bugs above; `EntitiesCatalogAuthoringTests` paying `DeleteAsset` +
  `CreateFolder` + `Refresh` per test for seven pure string tests (now once per fixture,
  33 ms instead of 250); `AssetConventionsTests` enumerating the whole `Assets/` tree seven
  times (now cached); and genuine work that is the point of the test — real
  `AssetDatabase.CreateAsset` in the catalog-authoring and `MonsterFramesImporter` fixtures
  (270-600 ms each), the deliberate 200-rewrite race in `AtomicWriteConcurrencyTests`, and
  the source-tree guards (`InputCentralizationGuard`, `ShippedScriptBinding`,
  `ResourcesScriptIntegrity`, `TileSeamPolicy`, ~1 s each, one test apiece). The one
  remaining lever is the four `MainMenu*` fixtures, ~45 tests at 200-230 ms because each
  builds the whole menu (`BuildOptionsSubmenu` alone is 81 ms) and they test transitions
  between its panels — ~9 s, 7 % of the suite, left alone because sharing one menu across
  tests is the order-dependent-flake shape this file already records twice.**
- **A hand-rolled parser with `while (true)` and no end-of-input check is a hang waiting for
  the first corrupted file.** `MiniJsonRuntime.Parser.NextChar` cast `StringReader.Read()`'s -1
  to `'￿'` — not a quote, not a backslash — so `ParseString` on an unterminated string
  appended it to a StringBuilder forever, and `ParseObject` / `ParseArray` spun on
  `while (true)` with nothing guaranteeing progress, so `[1,` was `list.Add("")` until memory
  ran out. Measured: `Deserialize("{ this is not valid json")` took **20.3 s and died of
  OutOfMemory**. Eighteen loaders parse hand-editable `StreamingAssets` files through that
  class — maps, buildings, zones, spawners, particles, FSM — so any of them corrupted by a
  stray keystroke froze the game the same way. The only symptom for months was ONE FSM test
  (`SaveSets_RefusesWrite_AfterMalformedSetsJson`) quietly costing 20 s of every suite run,
  passing, with the OOM swallowed by `LogAssert.ignoreFailingMessages`. The parser now
  carries a `_failed` flag, an explicit `AtEnd`, a string parser that requires its opening
  quote and fails at EOF, containers that fail at EOF or when a child fails, and an empty
  token that fails instead of being returned as a value the enclosing loop never consumes.
  The Editor twin (`Valkur.Editor.MiniJson`, index-based) had the same family through a
  number parser that consumed nothing; it got a "no progress → null" guard in both loops.
  `MiniJsonMalformedInputTests` pins seventeen broken inputs under a 250 ms hang guard AND the
  leniencies the old code had (trailing garbage, bare words, trailing commas), so the fix
  cannot quietly turn a months-old file into one that stops loading. Two lessons travel:
  a `while (true)` in a parser needs a visible reason it makes progress, and a test that
  passes in 20 s is a bug report nobody filed — read the per-test durations, not the count.
- **An `execute_code` snippet is a METHOD BODY, so a `using` directive in it is a parse
  error — and the knock-on is that extension-method syntax does not resolve.** Measured on
  this bridge (which reports `"compiler":"codedom"`): a leading `using System.Linq;` fails
  with ``Unexpected symbol `System', expecting `('`` — the compiler is reading it as a
  `using` STATEMENT, which is what a method body may legally contain. It is NOT that LINQ is
  unavailable: `System.Linq.Enumerable.Count(xs)` returns 3 on the same array that
  `xs.Count()` cannot reach, because the extension form needs the directive the body cannot
  have. String interpolation works too, so "it is C# 6" is the wrong summary. Write
  fully-qualified static calls and plain `foreach`, and keep `var` for locals.
- **A nested type must actually be nested.** Related and easier to get wrong:
  `Valkur.Data.SpellDefinition.SpellCastAnchor` does not compile because `SpellCastAnchor`
  is a TOP-LEVEL type in `Valkur.Data` that happens to be declared in `SpellDefinition.cs`.
  The error names the nested type, not the file, so it reads like a stale assembly. Resolve
  an uncertain type through `System.Type.GetType("Namespace.Type, Assembly")` first and let
  it tell you where the type really lives.
- **A probe that flips global Editor state and throws before restoring it has changed the
  Editor for every test that follows.** An `execute_code` timing probe set
  `Debug.unityLogger.logEnabled = false` (to keep log capture out of a Stopwatch), then hit a
  `TargetInvocationException` on the next line — with no `try/finally`, the restore never ran.
  Nothing announced it. The next run of a group whose tests use `LogAssert.Expect(LogType.Warning,
  ...)` came back red with "Expected log did not appear", on tests that were fine, in fixtures
  that had nothing to do with the probe — the mechanism is invisible from the failure. Same
  family as the static-event and `SetInvincible` traps: a global toggled from a place that does
  not own it. Rules that follow: any probe that changes `Debug.unityLogger`, `LogAssert`,
  `EditorApplication`, `Time`, or a `ScriptableSingleton` restores it in `finally`, every time;
  and a red `LogAssert.Expect` after a session of probes is a reason to check
  `Debug.unityLogger.logEnabled` BEFORE reading the test.
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
  `WorldTransitionService.IsOverlayLoadable` FIRST; the Buildings editor's Door panel and the `door`
  console command refuse an unloadable target at author time for the same reason.
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
- **A building's Z IS a tile layer, and there is exactly one sorting slot per layer for it.**
  `BuildingObject.ZBottom` / `ZTop` (0..15) name the painted visual layer each half sits
  directly above; `SortingConfig.PropSortingLayer(z)` answers `PropsL{z}`, a sorting layer
  INSERTED between the tiles of layer z and those of layer z+1. So "does this building draw
  over that wall" is decided by sorting-LAYER comparison, by name, and `sortingOrder` carries
  only the Y-sort. Defaults 4 / 6 sit either side of `Entities`, which is the whole split
  ratio: the player walks between a trunk on `PropsL4` and a crown on `PropsL6`.
  **It used to be a signed TIER** multiplied by `Z_TIER_SCALE` into the order and promoted
  to `WallsTop` on its sign — a ladder that ended at `WallsTop`, which is BELOW the layer-7
  and layer-8 tile slots, so no value of Z could put a building over a wall painted up there
  and an author pressing "+" got nothing, silently. Measured: 4 of 301 shipped placements
  used it, i.e. what a control that cannot do the job looks like in data. The tier also
  produced this project's one sort-key overflow (a five-digit order wrapping the 16-bit
  short — and the wrap is worse than that incident recorded: it happens in the SETTER, see
  the sortingOrder ceiling below);
  nothing is multiplied now, so it cannot recur. The JSON keys are `layer_bottom` /
  `layer_top`; the old `z_bottom` / `z_top` are still READ through the sign rule the old
  code applied (`BuildingLoader.LegacyZBottomToLayer` / `LegacyZTopToLayer`), so those four
  rows come back where they were authored, and the next save rewrites them.
  **A first cut shipped TWO dials — the old Z plus a separate "Capa" — and was rejected in
  review within the hour**: two controls that both mean "depth" is a design where the one
  drawn on the building is the one that cannot do the job. One number, one meaning.
  Pinned by `BuildingZLayerTests` (the ladder, by live `SortingLayer` values, never literals)
  and `BuildingLoaderZOffsetTests` (both key generations).
- **Editing `ProjectSettings/TagManager.asset` on disk while Unity is open does not edit
  TagManager.** Unity holds it in memory like any asset, a domain reload does not re-read
  it, and a later save — or a Play-mode round trip — flushes the STALE copy back over the
  file. Measured twice in one afternoon: nine sorting layers written to disk, `refresh_unity
  (scope=all)` reported ready, `SortingLayer.layers` listed them, and one Play/Stop later
  `GetLayerValueFromName("PropsL8")` answered **0** — `Default` — which puts the renderer
  behind the entire world with nothing logged, and the file on disk was back to its old
  contents. Add sorting layers through the live object: `LoadAllAssetsAtPath
  ("ProjectSettings/TagManager.asset")`, a `SerializedObject` over `m_SortingLayers`,
  `InsertArrayElementAtIndex`, then `EditorUtility.SetDirty` AND `AssetDatabase.SaveAssets`
  — without the `SetDirty` the second batch here stayed in memory only and was lost on the
  next Play. Verify by reading `SortingLayer.layers` and the file's own text in the same
  probe. A test that resolves a name through `GetLayerValueFromName` must first assert
  `NameToID(name) != 0`, or a lost layer passes as "value 0" on the wrong side of everything.
- **A sorting layer the ambient light does not reach renders every LIT sprite on it BLACK —
  and the mask was an ALLOWLIST.** Nine `PropsL*` sorting layers were added for buildings,
  every building in the world went black at 08:27 with the tiles around them lit, nothing
  logged, and the wiring test passed: `TheSceneLightIlluminatesEveryWorldSortingLayer`
  compared the scene's mask against the same twelve-name table the boot wrote, one half
  against itself. An allowlist has to be remembered every time TagManager grows; the mask is
  DERIVED now — `GameplaySceneSetup.AmbientLitSortingLayerNames()` is every layer in
  `SortingLayer.layers` minus `AmbientUnlitSortingLayers` (`Projectiles`, `VFX`, `UI_World`,
  `Overlay`, each with a reason). A new layer is lit by default, which is the failure that is
  VISIBLE (noon-bright at midnight) rather than the one that deletes half the world.
  `AmbientLitCoverageTests` pins the property from the other side: every `PropSortingLayer(z)`
  and every slot `VisualLayerSortingSync` can put an entity on must be lit, every denylist
  name must exist (a typo there is a layer that quietly goes lit), and lit ∪ unlit must be
  exactly TagManager. That second check found a latent one the same hour: visual layer 7 sent
  entities to `Projectiles`, an unlit slot — nothing had ever climbed there (zero layer-jump
  tiles shipped) — so it has its own `EntitiesHigh` now. The scene's authored mask is kept
  complete as well: it is the fallback for the day the boot's reflection write cannot land.
  **And TagManager edits only land OUTSIDE Play Mode**: `SetDirty` + `SaveAssets` during Play
  left the file untouched and the entry vanished on Stop — the third loss in one afternoon.
- **`SpriteRenderer.sortingOrder` is a 16-bit short IN THE PROPERTY SETTER, so the Y-sort has
  a world-size budget of ±317 units.** Measured directly, not inferred: writing 32768 reads
  back **-32768**, 65536 reads back **0**, 100000 reads back **-31072**, 300000 reads back
  **-27680**. The field does not keep what you write, so an order past the short window is
  not "very high" — it comes back NEGATIVE and the sprite draws behind everything it was
  meant to be in front of. This became load-bearing the moment the building Z stopped being a
  multiplied tier: `sortingOrder` now carries the Y term and nothing else, so the world's Y
  span IS the budget. `SortingConfig` states it as `Y_SORT_SCALE` (100), `SORT_ORDER_HEADROOM`
  (1024 — `Z_UI` is 1000 and two callers nudge by ±1, so the Y term must leave room or the SUM
  wraps while the Y term looks fine) and `MAX_SAFE_WORLD_Y` = **317**. `YToSortingOrder`
  CLAMPS and warns once: past the line every renderer out there sorts as equal — locally wrong,
  bounded, still monotone up to the line — where wrapping would put the furthest thing in the
  world in front of everything. The shipped world runs `offset_y` 0..100 tiles over 50-tall
  zones, i.e. 0..150 units, about 2x headroom — and `zones_database.json` AUTO-EXPANDS, so a
  new zone row is how this gets reached, not a decision anybody makes.
  `WorldLayerCeilingTests.EveryShippedZone_SitsInsideTheYSortBudget` reads the shipped file.
- **The ladder is SIXTEEN visual layers, which is Unity's own ceiling, and reaching it spent
  every free physics slot in the project.** `SortingConfig.VISUAL_LAYER_COUNT` is the single
  literal; `CollisionTagMap.LayerCount`, `WorldCollisionLayers.LayerCount`,
  `VisualLayerOccupant.MaxLayer`, `VisualLayerProbe.LayerCount` and `LayerJumpMap.MaxTarget`
  derive from it. It lives in Core because `TilemapLayerSetup.TilemapLayer` — the enum that
  morally owns it — is in Gameplay and Core may not reference Gameplay, the same constraint
  `LoadoutStateSheets.state` answers; the two are pinned against each other in BOTH directions,
  plus contiguity from 0, because every consumer indexes arrays and bitmasks by the raw value.
  Layers 0..8 keep the names the Python build gave them (`Ground`, `WallsTop`, …) because those
  strings are written into every shipped overlay file; 9..15 are named by INDEX (`Tier9`…),
  which is the honest name for a tier whose job nobody has decided.
  The three ceilings, measured: **32 physics layers** — each visual layer needs a `WorldL{N}`,
  and the seven that were free are now spent, so **0 remain**. `WorldL9..WorldL12` took the
  ordinary user slots 28..31; `WorldL13..WorldL15` took **3, 6 and 7, which are Unity's
  RESERVED builtin range** — they accept a name through `SerializedObject` and `LayerToName`
  resolves them (measured), but Unity's own Inspector will not edit them, so the top three
  tiers are the ones deliberately put there: if an upgrade ever reclaims that range it costs
  the tiers nobody has painted yet rather than the middle of the ladder. **47 sorting layers**
  (`Tier{N}` / `PropsL{N}` / `EntityL{N}` per tier, inserted after `EntitiesOverhead` so no
  existing entry moved). And **an int bitmask** — `CollisionTagMap.FullLayerMask` is 65535 now,
  cap 31. `WorldLayerCeilingTests` asserts every one of those joins.
- **Growing the ladder broke three things that were sized `9` by hand, and one of them stopped
  the player moving.** `VisualLayerProbe.Sample` REFUSES a buffer shorter than the layer count
  and answers 0 **without filling it** — so a stale `new bool[9]` does not throw, it reports
  "no tile on any layer" for every cell. `PlayerController.Movement._voidSampleBuf` was one:
  `ClampInputAgainstVoid` then read the whole world as void and zeroed the input, i.e. the
  character could not move at all. `TileEditorManager._underfootScratch` and
  `TileEditorUI._layerVisibility` were the other two, the second also indexed by layer against
  a nine-long array. All three size from `SortingConfig.VISUAL_LAYER_COUNT` now. The suite
  caught all of it (`PlayerVoidStopMovementTests`, `VisualLayerProbeTests`), which is the
  argument for the derived count: a literal that is merely *stale* fails loudly, a buffer that
  is stale fails silently one layer down.
- **A collision tag is a NUMBER now, not a character, and that is what made layers 9..15
  taggable.** `CollisionTagMap` emitted one char per layer (`'0' + i`) and `Canonicalize`
  rejected any two-digit run outright, with a comment saying it kept the schema "tight to the
  0..8 enum range" — true, and it is exactly why layers above 8 had no spelling at all once
  the ladder grew. A segment is a RUN OF DIGITS read as one decimal now, so `"12"` is layer
  twelve and `"1,2"` is layers one and two: the comma is what separates them. Safe because
  the writer has always emitted commas (`"0,2,5"`) and the shipped maps carry **zero**
  collision tags, so no comma-less run like `"358"` exists to be re-read as one number. It
  also retired a SECOND parser — `LayerMaskFromTag` walked the canonical string character by
  character with its own `'0'..'8'` bound, which is the shape that lets two readers disagree
  about one string. Both go through `TryParseMask`.
- **A test pinned to the old size reports a correct build as broken, and thirteen did.** Every
  one asserted a literal `9`, `10` or `"0,1,2,3,4,5,6,7,8"`: `ValidTags` is ten entries, `"9"`
  is invalid, `EnumerateLayers` yields nine, an out-of-range clamp lands on 8, `ZTop = 12`
  clamps. All of them are derived from the count now. The distinction worth keeping: those
  thirteen were RIGHT to go red — the ladder really did change — and the fix is never to
  re-pin them to the new number, which only moves the same failure to the next growth.
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
  pack claiming `rock` is simply unreachable from the Tile editor's auto-brush, silently. `rock_lava`
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
- **`animation_map.json` reaches no runtime code.** The FSM editor's Animations panel and
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
  `VortexFieldExecutor` read `if (ctx.Spell.spawnAtMouse || isPull)`, so clearing the box in the Spells
  editor changed nothing for half the spells that carry it — the panel showed a control that could not
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
  `Light2D` hanging under it rendered at `authored x lossyScale`. **All three sites are clean now** —
  `VortexFieldController` when this note was written, `TotemController` and
  `PuddleController` in the 27-spell expansion. The note stays because the pair is what a
  rig-plus-radius API invites, and `AreaFXRig.Attach` still takes a radius, so the next
  controller written against it reintroduces it in two lines. A rig that wants a world size gives its children ABSOLUTE sizes and
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
- **The InputSystem mouse does not always freeze at ZERO, and a frozen non-zero position
  is the one that wins.** `MouseInputManager.TrySelectScreenMousePosition` prefers the
  InputSystem whenever it is finite and in view, with one guard — a stale `(0,0)` while
  legacy reads something else. Measured live (2026-09-05, in the player loop via
  `InputSystem.onAfterUpdate`, NOT from `EditorApplication.update`, which reads a different
  state buffer and lied): under the 2022.3 event-drop bug the device sat at the screen
  CENTRE `(800,400)` and at its last delivered position — finite, in view, plausible — so it
  beat the correct legacy reading, the cursor resolved to the player's own feet, and every
  aimed spell flew straight down while the pointer sat elsewhere. It is intermittent because
  it depends on which value the device stopped on; a session where it froze at zero "works
  perfectly", which proves nothing. `MouseFreezeTracker` turns the per-frame guess into a
  verdict from evidence (legacy moved across 3 distinct frames, InputSystem did not) and the
  selector yields to legacy while it stands; the verdict clears the moment the InputSystem
  moves. Distrust never invents a position — legacy out of view still answers false. Pinned
  by `MouseFreezeTrackerTests` and the `Frozen*` cases in `MouseInputManagerTests`, including
  a source check that the production path feeds the tracker, because the pure halves being
  green is exactly the shape that shipped broken. Two things this cost before it was found:
  a throttle on the redirected editor click was blamed first (it only made the single true
  trajectory visible), and a real but SMALLER defect — aiming from the sprite centre while
  firing from the hands, 0.42 u off — was fixed in `SpellTargeting.ResolveAimDirection`
  along the way.
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
  RESTORED, never cleared.** The dev console's god mode, the Spells editor's test invulnerability
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
- **A swatch that reaches the flourish must be reachable in the Spells editor.** Making
  `SpellCastFlourishFX` read `particleColor` made the field LIVE for every type that shows a
  flourish, but `SpellFieldRelevance` exposed it for only 15 of 26 — so on Meteor, Aura,
  Totem, Summon, Mine, VortexField, ArcaneFlame, Smoke and SmokeEmitter the colour now drove
  the gather while the panel hid the control for it. The relevance test only fails the other
  way round (a field an EXECUTOR reads and the panel hides), so nothing caught it. All nine are
  exposed now; `WeaponLoadout` and `AnimationProbe` stay hidden, correctly, because `AppliesTo`
  refuses them. Note the meteors themselves were never violet — `MeteorMissileFX` hardcodes its
  own fire colours — so only the cast read wrong, which is exactly why it survived unnoticed.
- **A RENDERED FRAME IS THE ONLY TEST FOR A LAYOUT, and this repo's own uGUI note says why.**
  The Controls editor shipped a window that could not be read: the board panel was invisible,
  the action list hung off the top of the screen with its header and search box above the
  canvas, and GUARDAR and VALORES POR DEFECTO were printed one letter per line down a
  one-character column, overlapping each other. Every structural probe was green — sibling
  order, panel sizes, row counts, 914 passing tests — because **uGUI performs no layout in
  EditMode**, so reading back `sizeDelta` returns the number that was written and never what a
  layout pass would have made of it. Four separate defects hid behind that, and each is worth
  recognising on its own:
  - **`UIFactory.MakeScrollView` puts a `VerticalLayoutGroup` AND a `ContentSizeFitter` on the
    content it returns.** Right for a list of rows, fatal for anything placed by hand: it deals
    absolutely-positioned children out in one column and forces their width. Three surfaces
    here used it — the drawn keyboard, the context strip, the legend — and all three carried a
    comment claiming there was no layout group on them. A comment is not a removal.
  - **Its content is STRETCH-anchored with a centre pivot**, so `sizeDelta.x = 1066` makes a
    rect 1066 px WIDER THAN ITS PARENT — measured at 2072 — and a horizontal scroll has
    nothing sane to scroll. Hand-placed content wants `anchorMin == anchorMax` at the top-left.
  - **`ApplyPanelDock` negates the offsets it is given**, so `TopLeft, 16f, -56f` docks the
    panel fifty-six pixels ABOVE the canvas. Every other editor passes a positive gap
    (`Camera.TOP = MENUBAR_H + GAP`, `Boss.PANEL_TOP_OFFSET`); this one passed a negative and
    lost both panels' headers off screen.
  - **A button in a `HorizontalLayoutGroup` with `childControlWidth` and no
    `childForceExpandWidth` is laid out at its MINIMUM unless it declares a width.** Two
    unstyled buttons collapsed to a single character column and overprinted each other.
- **A panel a runtime editor cannot reopen must not be closable, and `DraggablePanel` says so
  in its own API.** `ShowCloseButton` exists for "a panel whose editor has no other way to
  bring it back" — which is exactly the Controls editor, whose only toolbar lives INSIDE the
  board panel. It shipped closable, the workspace layer faithfully persisted `"open": false`,
  and the editor then opened forever with no keyboard, no mouse, no tabs, no legend and no
  status line. A workspace document on this machine was found in that state, which is what
  "the window makes no sense" turned out to mean. Two fixes, both needed: the close button is
  gone, and the editor forces both panels open on restore so documents already poisoned heal
  themselves.
- **Persisted panel geometry can DRIFT, and each session saves the drift.** Measured across
  three consecutive opens of the Controls editor: a panel docked at exactly 1092x424 came back
  as 1089.20x545.34, and the list panel walked from 16 px inside the right edge to flush
  against the LEFT one. Several passes each legitimately adjust geometry on restore — the
  workspace service's off-screen rescue, `DraggablePanel`'s anchor normalize a frame later,
  its canvas-resize clamp — and `CaptureOnClose` records whatever they left, so the error
  accumulates rather than settling. A layout-version bump does not fix it: the drifted
  geometry is simply saved under the new version. The Controls editor now re-docks to its
  defaults three frames after restore and declines to remember geometry at all — one editor
  opting out, not a change to the layer. **Anything that sets a panel's size during restore
  must land after those passes, or it is setting a value rather than keeping one.**
- **`UIInputField.AddCommit` builds NO placeholder**, only `MakeWithPlaceholder` does — and
  that one takes no commit callback. Setting `.placeholder`'s text on a field from the first
  is a write to null, and the row renders as a bare dark bar with nothing saying it is a
  search box. Build the child yourself and assign it.
- **A drawn control is invisible when its fill matches the surface behind it.** `INPUT_FREE`
  (0.14) sat on `BG_SURFACE` (0.13) — one percent of a channel — so every unbound key vanished
  and half the keyboard had no keys on it, which is precisely the question ("what is free") the
  drawn board exists to answer. The drawn mouse's shell had the same collision and read as
  buttons floating in the dark. `INPUT_BOARD_BG` and `INPUT_DEVICE_BODY` now separate the three
  layers.
- **On a 34 px key cap the OS legend is the wrong string.** `InputControl.displayName` is the
  only thing that knows a Spanish ISO board prints n-tilde where Unity says `semicolon`, and
  every such legend is one or two characters. The long ones are where it is unhelpful: it
  answers "Print Screen", "Scroll Lock", "Page Down" and "Numpad 7", which truncate to
  "Print Scree", "Scroll Loc" and a numpad where every key says "Numpad". Ask the device for
  SHORT legends and fall back to the project's compact table ("Impr Pant", "Av Pag", "Num 7")
  for the rest — the device owns what a key PRINTS, the table owns what it is CALLED. Pair it
  with TMP auto-sizing rather than `TextOverflowModes.Ellipsis`: shrinking a long legend costs
  size on keys nobody reads, truncating costs the word.
- **A full-screen scrim that invites a click must be the FIRST sibling, not the last.** The
  Controls editor's capture overlay shipped last: full-screen, a raycast target, carrying a
  Button that cancels — over the drawn keyboard whose caps its own prompt told the author to
  click. Every click on a key cancelled the capture instead of completing it, and the whole
  feature the panel exists for was unreachable. It was INTERMITTENT, which is worse than
  broken: `DraggablePanel.OnPointerDown` calls `SetAsLastSibling`, so an author who had dragged
  the board panel once had raised it above the scrim and the click DID land — the bug came and
  went with a gesture nobody connects to it. The shape that works is three objects with three
  jobs: a scrim at sibling 0 (behind the panels, click-anywhere-to-cancel), the panels, and a
  prompt banner raised to the top with `raycastTarget = false`. Re-assert the order on every
  open, because one click on any panel reorders all of it.
- **A capture poll that reads `Keyboard.current` directly cannot bind a mouse, and dies where
  it is needed most.** The same overlay's poll walked `InputControlPaths.Entries` against the
  raw device, so no mouse button could ever be assigned — and the raw InputSystem half is
  exactly the one that stops delivering under the 2022.3 event-drop bug, i.e. in the session
  where the player went looking for the Controls editor BECAUSE their keys had stopped working.
  `KeyboardInputManager.WasKeyPressedThisFrame(Key, KeyCode)` ORs both backends and honours
  `InputBlocker`; the mouse goes through `MouseInputManager`. The left button is deliberately
  NOT polled — it is how the author clicks the drawn board, so polling it would bind LMB to
  whatever they were pointing at; left click is bound by clicking the drawn mouse's own left
  button. `InputCentralizationGuardTests` was blind to all of this because its regexes only
  matched `kb.<name>Key.<state>` and the capture used the INDEXER form `kb[key]`; the indexer
  pattern is in the guard now.
- **A rebinding panel that writes slot 0 applies a fraction of what it says.** `Move` carries
  eight controls (WASD plus the arrows) and `Dash` four; the shipped editor hardcoded binding
  index 0, so "rebind Move" moved the W and left seven keys where they were, silently. Rows
  expand into one row per SLOT now, each with its own assign and clear, and the ordinal is
  translated to an index that skips composite headers — an override that lands on the
  `2DVector` header moves nothing while reporting success.
- **A conflict scanner that is map-aware but not CONTEXT-aware trains the reader to ignore
  it.** Painted from `InputConflictScanner.Scan`, the Controls board rang FIFTEEN keys red in
  the War view — WASD, the arrows, all three mouse buttons, space, Escape — because each is
  both a gameplay verb and a UI verb, which is how the project has always shipped and has never
  been a bug: only one consumer is listening at a time. Beside them sat a summary line reading
  "Sin conflictos reales" in green. Fifteen permanent false positives next to a contradicting
  summary is an alarm nobody reads. `ClashesInContext` / `Classify` grade each control in the
  context being viewed and only a real double fire is red; measured after, every context is
  0 red. Three things had to become DATA for that to be honest rather than merely quiet:
  `RequiresCtrl` (Ctrl+S saves and bare S picks the select tool — five actions carry it, and
  `EditorInput` reads the same field so the two cannot drift), `CoexistGroup` (Escape closes
  the editor AND opens the launcher by design), and the UI map being arbitrated by focus.
- **`EditorInput.Tool` refuses while Ctrl is held, and that closed a real double fire.** Every
  shared editor shortcut is Ctrl+key with the Ctrl living in C#, and no editor tool is — so
  a bare key and a Ctrl+key were competing for the same press. Measured on the shipped asset:
  `EditorShared/Save` and `Editor.Tile/ToolSelect` are both on `s`, so Ctrl+S in the Tile
  editor saved the map AND switched the active tool to Select, every time, in silence.
- **A search box that rebuilds its list is a hitch per keystroke.** The Controls editor's 63
  rows carry up to five buttons and seven TextMeshPro components each; building them measures
  213 ms, and the shipped code paid it on every character typed. Rows are realised once per
  CONTEXT and shown or hidden after that — 213 ms became 10 ms. Same shape and same fix as the
  Items editor's 3.5 s table, at a tenth of the scale. Measured per-component: `MakeButton`
  0.298 ms, a bare TMP 0.154 ms, a bare Image 0.068 ms — the cost is uGUI, not the row logic,
  so the only lever is building fewer of them less often.
- **`Object.Destroy` is an ERROR in Edit Mode, and a runtime editor's list rebuild hits it.**
  Not a warning: seven `ControlsEditorTests` went red on the log line alone, with every
  assertion passing. Any editor path that destroys UI — a row list, a tab strip, a redrawn
  board — needs the `Application.isPlaying ? Destroy : DestroyImmediate` branch the Entities and
  Buildings editors already carry.
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
  `interruptible`, `lockCastDirection` and `allowOverlap` have zero readers outside the Spells
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
- **A Button's tint is its ColorBlock, never its Graphic — and assigning the block does not
  repaint.** Two bugs, opposite directions, same afternoon, both invisible to every EditMode
  assertion and both found only by reading pixels out of a rendered frame.
  `Selectable` on the default ColorTint transition drives its `targetGraphic`'s CanvasRenderer
  to `colors.normalColor`, and the CanvasRenderer colour MULTIPLIES with `Graphic.color`. So
  writing `btn.targetGraphic.color = x` renders `x * normalColor`, darker than either: measured
  in the Skills editor, an "active" row painted ACCENT over `UIButton.Make`'s BTN_NORMAL came
  out **(32,31,29)** against a **(31,33,42)** panel — invisible — while untouched rows sat at
  BTN_NORMAL squared, near-black. The selection read BACKWARDS, the chosen row looking like a
  gap and the unchosen ones looking solid.
  The obvious fix is the second bug. Setting the block and pinning the graphic to white is
  correct arithmetic and still renders nothing, because `Selectable` pushes the block to the
  CanvasRenderer only when it EVALUATES a state transition — on enable, or on a pointer or
  selection event. A button whose block changes while it sits idle keeps whatever the
  CanvasRenderer last held, which after pinning the graphic is WHITE: measured, every button in
  the same editor came back at **(202,202,205)**, pale cream, just as unreadable.
  `UIButton.SetTint` is the single correct path — block, graphic held white, and an
  `enabled` toggle to force an instant transition. After it, active-vs-inactive luminance went
  from **3 to 108**. Note `CraftingPanelUI` was never affected: its local button factory never
  assigns a ColorBlock, so Unity's default white block made the multiply a no-op — the trap
  only bites buttons from `UIButton.Make`.
  **Neither half is testable in EditMode.** A component added there never receives `OnEnable`,
  so the CanvasRenderer holds its construction value whichever branch the code takes, and an
  assertion on `colors.normalColor` passes with the window unreadable. `SkillsEditorTests` says
  so in the fixture rather than implying more reach than it has.
- **An `execute_code` probe that mutates a shipped asset has until the end of Play Mode to put
  it back.** The idle probe every session shares answers "can a run start"
  (`TestRuns.Count == 0 && !isPlayingOrWillChangePlaymode && !isCompiling`) — it does NOT answer
  "is it safe to write", and those are different questions. Measured: a probe set
  `paella.requiredLevel` from 1 to 4 to photograph a UI state, Play Mode ended first, and
  `SkillsRuntimeEditor.Instance` was null — so the editor's own undo was gone and the only
  recovery was writing the value back and verifying BOTH the loaded object and the file's own
  text, because a domain reload does not reload assets. Prefer a probe that reads; if it must
  write, restore in the SAME call, and verify on disk rather than in memory.

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
  (Particles, Spells, Items, Tile) are top-left-pivoted and none of them passes one.
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

- **`AuraExecutor` divided `radius` by 16 — the SIXTH Python-pixel sighting**, after
  `wallWidth`, the totem's radius, the vortex's radius, `coneLength` and `arcane_flame`'s
  radius. What kept it alive for the life of the project is that the wrong number was never
  READ: `InitializeHealing` took `gameRadius` and immediately discarded it with
  `_ = gameRadius; // reserved for future "heal nearby allies" logic`. Shipped `healing_aura`
  authors 0.625, which resolved to a gameplay radius of **0.039 world units** — under a
  fortieth of a tile — and nothing anywhere consulted it. A dead field cannot be wrong on
  screen, which is exactly why the divide survived five rounds of the same bug being found
  elsewhere. Grep for a discarded parameter whenever a unit conversion looks suspicious: the
  conversion is the tell, the discard is the reason nobody noticed.
- **`TotemController` healed exactly one entity: `_owner`.** It measured the distance from the
  totem to its caster and healed only them, so `healing_totem` was a stationary self-heal with
  a decorative pole — and the ring it drew on the ground promised an area that no code
  consulted. Same family as `animation_map.json`: authored, drawn, round-tripped and inert.
  It now sweeps the Player layer with `OverlapCircleNonAlloc`; "friendly" is answered by that
  layer plus the caster rather than by a faction system, because a second allegiance model
  beside `AlliedUnit` is two models that will eventually disagree. The same method also
  carried the `AreaFXRig.Attach(...)` + `transform.localScale = radius` pair that rendered the
  vortex's `Light2D` at an effective 367 units. `PuddleController` carried it too and was
  fixed in the same pass, which retires the last site.
- **No state class reads `stats.faction`, so a summon spawned through the monster pipeline
  hunts the player who cast it.** The faction string is authored on every shipped entity and
  read by exactly one line in the project (`FSMMonsterBrain` pushes it into the FSM context,
  where no shipped set tests it). What actually decided allegiance was that all twenty target
  lookups across Idle/Patrol/Chase/AlertChase/Attack/Flee/NPCCast/FSMTransition asked
  `EntityRegistry.Player` directly. They now go through `FactionTargeting.EnemyOf(seeker)`,
  which answers `EntityRegistry.Player` unchanged when no `AlliedUnit` is alive — an empty-list
  count check — and otherwise lets a monster retarget to a nearer ally and an ally hunt the
  nearest non-allied monster. The alternative, a dedicated `Monster_Ally` FSM set, forks the
  state classes; one helper plus a mechanical substitution also buys monster-versus-monster
  fights, boss adds and charmed enemies. **A green monster suite does not validate this**: no
  existing test puts an ally on the field, so the whole suite only exercises the ally-ABSENT
  path. `FactionTargetingTests` asserts the composition, which is the half that can be false
  while both halves read correctly — the same shape as `SPAWNER_COORDINATE_SPACE_DRIFT`.
- **A note about a trap does not stop the trap; removing the wrong shape does.** CLAUDE.md
  already said, verbatim, that a `static readonly` array cache cannot be reset in a form
  `DomainReloadStaticResetTests` recognises — and three new `private static readonly
  Collider2D[]` scratch buffers were written in a single batch anyway (homing acquisition, the
  damaging aura, the healing totem), all three failing the ratchet, because that declaration is
  simply what a `NonAlloc` query looks like and nobody reads a warning about a trap they do not
  know they are approaching. `Gameplay/Combat/PhysicsScratch.cs` now owns those buffers behind
  one reset hook, so a new area sweep BORROWS one instead of declaring it. The buffers are named
  per purpose rather than shared generically, and that is not tidiness: one buffer handed to
  every caller is correct for sequential queries and silently wrong for nested ones — a sweep
  whose loop body triggers a second sweep has its own results overwritten mid-iteration, with no
  exception and no log. `Projectile._sweepHits` and `_explosionHits` stay where they are,
  grandfathered in `unreset-statics.txt`; note that adding a THIRD line there was the easy move
  and the wrong one, because a ratchet that accepts new entries is a list.
- **A registry must not hand out its backing list, and membership is not eligibility.**
  `AlliedUnit.Live` returned `_live` itself while three paths mutated it, so
  `for (i = 0; i < allies.Count; i++)` re-read `Count` each iteration against something a loop
  body could shrink — indices shift, an entry is SKIPPED, nothing throws, and a monster ignores
  an ally standing in front of it. Same shape as the boomerang: neither half wrong alone. It
  snapshots now, returning a shared `Array.Empty` when nothing is out so the case that is
  virtually always true allocates nothing. The second half is subtler and bit on the first fix:
  pruning INACTIVE entries as well as destroyed ones made re-enabling an ally impossible outside
  Play Mode, because nothing calls `OnEnable` there and nothing put it back. Destroyed leaves
  the registry; deactivated stays in it and is filtered by whoever asks. `OnDisable` must not
  unregister either, or the Play-mode path disagrees with the Edit-mode one.
- **A charge fraction must resolve to 1, never 0, for a spell that does not charge.**
  `SpellContext` is a struct, so a newly added `ChargeFraction` defaults to 0 on every one of
  the ~70 casts that know nothing about charging — and an executor multiplying by it raw would
  have made every projectile in the game deal its MINIMUM damage, silently, and only in the
  spells nobody thought to re-test. `ChargeMath.Resolve/DamageMultiplier/ScaleMultiplier` are
  the single answer and return a neutral 1 for `IsChargeable == false`. Damage and scale are
  deliberately separate curves (2.6x against 2.0x on `charged_bolt`): one curve for both costs
  the spell the "small and sharp versus big and slow" reading that makes charging a decision.
- **`Health.SetVulnerability` has ONE owner and that is what makes clearing it safe.**
  `SetInvincible` has three independent writers (god mode, the Spells editor, the shield) and
  therefore has to be saved and restored — a defect that shipped twice. The vulnerability
  multiplier is written only by `VulnerableEffect`, and `StatusEffectManager.Apply` replaces an
  effect of the same type before applying a new one, so `OnApply`/`OnRemove` pair exactly.
  Adding a second writer reintroduces the save/restore problem. It is also applied LAST, after
  defense: applying it before armour would let armour eat the amplification, so a +30% curse
  would read as +30% on a soft target and near-nothing on the armoured one it exists to open.
- **A spell that claims a DEATH should mark the living target, not raise a corpse.** A
  corpse-raiser has to hook `Health.OnDeath`, find a body `deathDisappearTime` may already have
  despawned, and reconstruct its `MonsterDefinition` from something that no longer exists — a
  death registry, kept in sync, holding data about entities that are gone. `ThrallMarkEffect`
  inverts it: the mark is carried BY the creature, so at the moment of death every fact is
  still on a live GameObject, and being a `StatusEffect` gives it duration, refresh, immunity
  and cleanup for free. Two details are load-bearing. The definition is captured at APPLY time,
  because `DeathState.Enter` calls `Object.Destroy` on the owner the instant the FSM reaches it
  and a `Destroy` cannot be cancelled — so the raising spawns a FRESH creature from the same
  definition rather than reanimating the body, which sidesteps the race entirely. And C#
  snapshots an event's invocation list when it is raised, so the handler still runs even if
  something else clears the target's status effects from inside its own `OnDeath`.
- **An absorb shield is a different tool from a timed one, and the difference is legibility.**
  Invincibility for five seconds is a state the player can only wait out; "one more hit" is a
  resource they can spend. `ShieldController` takes an `AbsorbPool` (reusing `wallHP`, which
  already had the better name for "how much punishment this conjured matter takes"), drains it
  on `Health.OnDamageBlocked`, and breaks on the frame of the blow that empties it rather than
  on its own clock. The ripple strength is scaled by the BITE the hit takes out of what is
  LEFT, not by raw damage, so the same blow rings the shell harder as the pool empties and the
  last hit before the break is the loudest — which is the whole of how a player reads the
  remaining pool without a bar on screen. Zero keeps the historical pure-timer shield.

## Player character pipeline (2 directions)

`dwarf`, `barbarian`, `elven`, `vampire` and `mague` are built from **side-view art drawn in
ONE direction**, and mirrored. Which direction is a per-sheet fact to be measured, not
assumed — wave4 faces west, wave5 faces east, and every one of wave12's thirty-one mague
sheets faces west. Only `valkyrie` still runs on the legacy 8-direction strips. The two
pipelines coexist on purpose and have different owners:

| | wave3 (dwarf, barbarian, elven, vampire, mague) | legacy (valkyrie) |
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
- **A character's size is a PAIR — baked pixel height and PPU — and neither number means
  anything alone.** The pixel height is the only lever on QUALITY: every frame is resampled
  from a source cell 331-681 px tall, so the shared 115 px throws away three to five times
  linear, and measured on the vampire's idle the face, the choker and the gold filigree are
  simply gone at 115 and legible at 256. The RATIO of the two is the character's WORLD
  HEIGHT. So raising the pixel budget alone makes the character physically bigger, and
  raising the PPU alone shrinks her — one edit, two properties, silently. `PLAYER_BODY_PX`
  and `PLAYER_PPU` in `build_player_frames.py` are therefore declared together, and the
  vampire, the mague and the elven all ship **256 px at PPU 96 = 2.667 world units**, 1.48x
  the dwarf's 1.797, at 4.9x the texels. A character with no entry takes 115 / 64 and imports
  exactly as it always did — today that is the dwarf, the barbarian and the valkyrie, and the
  split is a design statement rather than a leftover: the short, heavy classes keep the
  original height and the tall ones share the vampire's.
  - **PPU 96 is not a taste value: it is the camera.** `CameraSetup` runs the game at ortho 5,
    which on a 960 px viewport is exactly **96 screen pixels per world unit** — so PPU 96 is
    one texel per screen pixel and PPU 64 is a 1.5x upscale. That is the whole difference
    between the two budgets on screen: at 2.667 units a character covers 256 screen pixels
    either way, and the only question is whether the texture has 256 texels to fill them or
    171 stretched over them. It is also why "as tall as the vampire" and "as sharp as the
    vampire" are two separate decisions, and why matching only the first leaves a character
    the right size and visibly softer than the one it was matched against.
  - **The PPU rides the MANIFEST, not the `.meta`**, for the reason `CharacterSpritePivots`
    already records: the builder rewrites the manifest for a whole player on every run, so a
    value stored beside the texture is silently correct until somebody rebuilds an unrelated
    sheet. `CharacterSpritePpu` reads the flat top-level `characterPpu` map and matches by
    `Art/Characters/<playerKey>/` PREFIX rather than by listing sprite paths — a frame on
    disk but not yet named in the manifest would otherwise import at the default and come
    back the wrong size. It is a top-level block rather than a field inside each player entry
    because those entries are read with a regex, and a nested object binds to whichever
    neighbour happens to be adjacent.
  - **`--only` must merge `characterPpu`, and that is the half that fails silently.** A
    player left out of a run contributes no entry, so rewriting the map with only what was
    built imports that character at the default PPU — resizing somebody nobody touched, with
    nothing logged. Same argument as the player-list merge directly above it.
  - **REBUILDING IS TWO STEPS AND THE SECOND IS SILENT.** The Python run rewrites 180 PNGs
    and the manifest; nothing reimports them. Measured live during this change: the pixels
    were 256 px and the metas still said 64, so the vampire rendered **4.047 units instead of
    2.667** — every number self-consistent, disagreeing only on screen, exactly the shape
    `SPAWNER_COORDINATE_SPACE_DRIFT` records. `refresh_unity(scope="all")` is not enough on
    its own either: if the textures are imported in the same pass that first compiles
    `CharacterSpritePpu`, the postprocessor runs before the class exists. Force-reimport
    `Art/Characters/<key>/` AFTER the compile lands, then re-run the atlas build — the
    packed sprite keeps the old PPU until the atlas is repacked, which is a third way to be
    stale. `PlayerFramesManifestBindingTests.EveryPlayerFrame_CarriesItsDeclaredPpu_AndTheDeclaredWorldHeight`
    asserts the COMPOSITION and catches all three.
  - **RAISING A CHARACTER'S BUDGET IS ONLY WORTH IT IF THE SOURCE CARRIES IT — measure before
    declaring.** 256 px buys detail only where the staged sheets are TALLER than that; below
    it the builder upscales, which costs 4.9x the atlas for no added texel of information.
    Measured on the elf's nineteen sheets before he was raised: 323 px (spellcasting_3) to
    723 px (the idle), so every one of them is a downscale. The mague's ran ~470. A character
    whose sheets came in under 256 must NOT be raised this way.
  - **The atlas capacity is a real constraint, not a formality, and it is what decides WHICH
    ATLAS a character lands in.** `characters.spriteatlas` holds the REMAINDER after
    `players.spriteatlas` claims the shared-budget folders — today the vampire, the mague and
    the elven, i.e. exactly the three baked at their own pair. Measured at 256 px: vampire
    11.9 Mpx, mague 35.1, elven 21.8, so the three span about four 4096 pages (16.78 Mpx
    each) while the three shared-budget characters fit ONE at 11.3. Packing them together
    would have taken `players.spriteatlas` past three pages AND put a 256 px character in the
    same draw as a 115 px one; splitting them instead made `players` smaller than it was
    before any of this. The pixel budget and the atlas assignment are chosen from that
    arithmetic, not from taste.
  - **The bill is real and it is VRAM, because these atlases are deliberately UNCOMPRESSED**
    (`SpriteAtlasBuilder` writes `RGBA32` + `Uncompressed`; compression artifacts on pixel art
    are the project's highest-impact visual regression). At 4 bytes a pixel the character
    atlases went 145 MB -> 251 MB -> 320 MB across the mague and elven raises. That is the
    number to quote before raising a fourth character, and the reason the short classes
    staying at 115 px is worth something beyond taste.
  - **`CharacterAtlasBuilder` derives its folder list from the PlayerDefinition catalog, so
    it will happily re-add a big character to the wrong atlas.** It asks
    `CharacterSpritePpu.PpuFor` and skips any character that declares its own PPU — asked of
    the manifest rather than a hard-coded name list, because a second list drifts silently:
    the next character baked large would be added to the manifest, import at its own PPU, and
    land in the wrong atlas. This closed a REAL divergence rather than a hypothetical one —
    the shipped `players.spriteatlas` never contained the vampire, but that method would have
    re-added her on the next run.
- **`AssetDatabase.GetAssetPath` returns EMPTY for an atlas-packed sprite**, so any test that
  resolves a packed sprite back to its source path checks nothing at all. Measured: 0 of 1016
  players and 0 of 180 characters resolved. It is the vacuous-fixture shape this file records
  for `EditorReachabilityTests` — worse than an absent test, because it reports coverage it
  does not have. Match a packed sprite by its NAME against the real folder list, longest name
  first so a key that is a prefix of another never wins, and pair it with a
  `Assert.That(inspected, Is.GreaterThan(0))` guard. The same fixture had a second vacuity: its
  `AtlasPath` constant names `players.spriteatlas` only, and the one character not on the
  shared PPU is not in that atlas — so it would have been green for the only character it
  needed to see. Both atlases are walked now.
- **A character with no idle binds NO ART AT ALL, and that one line decides which loadout is
  the base.** `EntityAnimationBinder.ApplyVisuals` opens with
  `if (!HasFrames(idleSet)) return false;` — walk, cast, death and the rest all fall back,
  idle does not. The mague's wave12 contains exactly ONE idle sheet and it holds his staff,
  so the staff is visible whenever he stands still, and no arrangement of the art avoids
  that. What the constraint DOES leave open is which half is the base, and the answer comes
  from the toggle rather than from the art: `PlayerLoadoutController.ToggleLoadout` treats
  putting a loadout ON as the DRAW (immediate, flare forward) and taking it OFF as the STOW
  (deferred, equip animation played REVERSED). Making the staff the loadout lands those two
  the right way round and reuses the shipped `weapon_toggle`, its innate grant in
  `SpellTreeSeeds.InnateSpellKeys` and its key binding untouched; making the staff the BASE
  would have inverted both directions and needed a second toggle spell, a second catalog
  entry, a second innate grant, a second input action and a second icon. So: base is the
  bare-handed wizard, `armed` is the staff, and the price is one wrong frame-set — the
  unarmed idle — which `MagueWave12RigTests.EveryBaseState_IsFilled_AndDrawnFromItsOwnFolder`
  pins so it fails the day six frames of an empty-handed idle are drawn.
- **A state that carries variants NEVER renders its base set.** `ResolveEntryVariant` always
  answers an index in range when `VariantCount > 0`, so a `state_variants` declaration
  listing only the ALTERNATES would silently retire the default cycle. The builder prepends
  the state's own sheet as variant 0 rather than asking an author to repeat it, which makes
  that impossible to get wrong and keeps index 0 the sheet the `states` map already calls
  the default.
- **Idle, walk, chase, damage, death and recover pick their variant on ENTRY; attack and cast
  are picked by the ACTION.** Nothing "casts" a walk, so there is no action to hang a choice
  off and the only moment that exists is the transition into the state — that is what
  `DirectionalAnimator.SetState(state, direction)` now resolves through `ResolveEntryVariant`,
  rotating (never randomising: a random pick repeats back to back one entry in N and reads as
  the animation having failed to change). Two guarantees hold it up and both are load-bearing.
  Nothing re-rolls mid-state — `PlayerController.Movement` re-asserts the walk every frame and
  every turn comes through the same overload, so a re-roll would restart the cycle on each
  step and the character would twitch. And a state with NO variants answers **-1**, never the
  previous state's index: the old body returned `_activeVariant` unconditionally, which was
  harmless only while Attack and Cast were the sole states carrying variants and
  `GetSpriteSet` falls an out-of-range index back to the base set — the moment walk carries
  four, a 3 left over from a spellcast selects walk variant 3.
- **A variant index of -1 is a CHOICE, not an absence**, and that is why the "have I entered
  this state yet" question needs its own flag (`DirectionalAnimator._stateEverSet`) rather
  than being inferred from the value. The tempting shortcut — re-select whenever
  `_activeVariant` is -1 — reads a deliberate answer as a missing one:
  `NPCCastState.ResolveCastVariant` returns -1 for a spell that reserves no animation,
  meaning *use the base set*, and `FSMMonsterBrain.OnFSMStateChanged` calls the two-argument
  `SetState` immediately after `Enter`. Under that shortcut every Dark twin (they inherit 3
  to 9 cast animations from the player class they wear) would have had its -1 overwritten
  with a rotation index and put back by the next `FaceTarget`, restarting the frame cursor
  twice for one cast. Same family as `particleColor`'s opaque-white sentinel and
  `scaleConfig.tint`'s alpha-zero one: a value that means something is not a value that means
  nothing. Pinned by `DirectionalAnimatorEntryVariantTests.AnExplicitMinusOne_IsRespected_*`.
- **`DarkRosterBuilder` carries every new `EntityAssetConfig` block onto the six dark twins
  for free**, because it copies the whole config through
  `SerializedObject.CopyFromSerializedProperty` rather than cloning field by field — which is
  exactly what that choice was for, and this wave is the first thing to exercise it. So
  `stateVariants`, `LoadoutStateSheets.variants` and `Loadout.attackVariants`/`castVariants`
  reached `dark_mague` with no edit to the builder, and the dark roster's locomotion now
  varies the same way the players' does. Re-run `Valkur > Monsters > Build Dark Roster` after
  any player re-import, or the twins keep swinging an animation the class no longer has.
- **A loadout REPLACES every rotation it touches, at all three levels.** `LoadoutStateSheets`
  gained `variants`, and `Loadout` gained `attackVariants` / `castVariants` of the EXISTING
  `AttackVariant` / `CastVariant` types rather than a parallel one. Without that the LOOK is
  scoped and the ROTATION is not, which is visibly wrong in both directions at once: the
  mague's five bare-handed casts would keep rotating while he is visibly holding a staff, and
  his three staff casts would put it back in his empty hands. An EMPTY list means "this
  loadout does not change how I swing", not "I swing once" — a loadout that only replaces a
  hat must not delete the melee rotation. Two consequences worth knowing: every state is
  pushed on a re-bind (`ApplyStateVariants` loops all of them, the same rule
  `ApplyStatePacing` follows) so a stale array cannot keep the staff walking after it was
  stowed; and **a loadout that overrides casting must RE-DECLARE any reservation it still
  needs**, `weapon_toggle` above all, because the sheathe is cast from INSIDE the loadout and
  a cast list that forgets it stows the staff to a spellcasting pose.
- **`stateVariants` may not name `attack` or `cast`.** They have their own lists with their
  own selection rules, and accepting them in both places means whichever install ran last
  wins, silently. Refused loudly by `EntityAnimationBinder.ApplyStateVariants` at bind time
  and by `PlayerFramesImporter.CountInvalidVariantGroups` at import time — two gates because
  the manifest and the Inspector are two ways in.
- **Unity's `JsonUtility` does not serialize a type that contains itself**, so a loadout's
  per-state variants ride BESIDE its states (`LoadoutEntry.stateVariants`, grouped by state)
  rather than nested inside each `StateSheetEntry`. The grouping is also why `StateVariant`
  carries no `state` field: it would mean something at the top level and nothing at all
  inside a loadout, and one redundant field on every variant of every character is what later
  gets set wrong and ignored.
- **The mague ships 30 of his 31 sheets.** Three walks, two hurts and three deaths rotate as
  state variants; four staff walks rotate inside the `armed` loadout; punch and kick are the
  bare-handed melee rotation while `staff_swing` is reserved to `slash_regular` (a slash is
  the one action that must never render empty-handed, so it is reserved rather than rotated —
  otherwise the staff appears on every third bare-handed swing). The one sheet held back is
  `mague_staff_unequip`, a purpose-drawn SHEATHE with no selector: the stow plays the equip
  variant REVERSED — one sheet, one motion, read either way — and a variant is chosen by
  spell key, so both directions of `weapon_toggle` resolve to the same one. Shipping it needs
  the controller to name a second variant on the stow, which is a runtime change rather than
  an art import.
- **The mague is baked at the vampire's pair — 256 px / PPU 96 = 2.667 world units — and
  packed beside her.** He shipped first at the shared 115 / 64, which made him 1.797 units
  against her 2.667, i.e. 67% of her height, and that is what the size was corrected from.
  Two things follow and neither is optional. The PPU had to move with the pixel height or the
  character would only have changed size, not sharpness: at 2.667 units he covers 256 screen
  pixels, and 171 texels stretched over them is the same 1.5x upscale the rest of the roster
  lives with. And he had to leave `players.spriteatlas`, whose four remaining folders now fit
  ONE 4096 page (11.3 Mpx, down from 24.5 before the split) while he, the elven and the
  vampire take about four in `characters.spriteatlas`. Verified end to end rather than from
  the declaration: his idle frame 0 measures 262 px at PPU 96 and the elf's 256, and rendered
  at the camera's 96 px/unit the roster comes out dwarf 1.79, barbarian 1.81, valkyrie 1.79,
  and vampire / mague / elven all at 2.67.
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

## The Dark roster (hostile twins of the playable classes)

Six hostiles wearing the player classes' own animation art, tinted to a silhouette:
`dark_dwarf`, `dark_barbarian`, `dark_elven`, `dark_mague`, `dark_valkyrie`, `dark_vampire`.
They are the first monsters in the project that **dodge**, and the first that **cast** at all.

```text
Valkur > Monsters > Build Dark Roster (Dry Run)  then  Build Dark Roster
  -> Data/Catalogs/Monsters/Dark/dark_<class>.asset  + MonsterCatalog entry
DarkRosterBuilder      Scripts/Editor/Monsters/     the generator
Monster_Dark           StreamingAssets/FSM/sets.json  the FSM set (no FleeState)
DodgeState             Enemies/FSM/States/          the lateral burst
FSMDodge               Enemies/FSM/                 the decision: gate, cooldown, heading
FSMThreatSense         Enemies/FSM/                 "is a bolt aimed at me and closing"
spawn dark_elven 1     DevConsole                   the way to look at one
```

- **The art is generator-owned; the design is authored once.** A dark twin's
  `assetConfig` is copied from its `PlayerDefinition` and REWRITTEN on every run, because
  every wave of `build_player_frames.py` moves that art and a hand-copied monster drifts
  silently — the twin goes on swinging an animation the class no longer has. Stats, AI
  tuning and the spell list are written only on CREATION and a re-run never touches them.
  The failure is asymmetric, which is why the line sits exactly there: stale art is
  invisible until someone plays the ENEMY, a reverted balance pass until someone plays the
  GAME. `DarkRosterDataTests` compares the two sprite for sprite.
- **The copy goes through `SerializedObject.CopyFromSerializedProperty`, never a
  field-by-field clone.** `EntityAssetConfig` has grown a recover slot, attack variants,
  cast variants, state pacing, a layout enum and loadouts, none of them with this file in
  mind. A copier that enumerates fields is a second list of them that keeps compiling and
  quietly stops carrying whatever was added last.
- **A DARKENING tint no longer goes in the material, and that is what makes black
  playable.** `EntityAnimationBinder` has two channels and they MULTIPLY: the HDR `_Color`
  (right for a saturated boost, which vertex colour would clamp) and `SpriteRenderer.color`,
  which is `SpriteTintStack`'s territory and carries every hit flash, burn, freeze and death
  fade in the game. A (0,0,0) tint downstream of the stack annihilates all of it — measured,
  a full white hit flash reached the screen as black, so the one signal saying "your shot
  landed" was invisible on exactly the enemies hardest to see. Any tint at or below 1 now
  lands on the renderer and is `Rebase`d as the stack's base, so the flash lerps AWAY from
  black. The rebase is explicit rather than left to the stack's lazy `Awake`, or whether it
  captured the tint or the white before it would depend on which effect attached it first.
  Every shipped LDR tint is opaque white, so nothing else changed. `EntityTintRoutingTests`
  pins the product of both channels, because each half looks correct alone.
- **The alpha of that tint is load-bearing.** `(0,0,0,0)` is the "nobody authored one"
  sentinel and resolves to WHITE — an alpha-zero black is not a dark entity, it is an
  untinted one. Same family as `particleColor`'s opaque-white sentinel.
- **Dodging is opt-in TWICE, and the second gate is structural.** `aiTuning.dodgeChance`
  defaults to 0, so `FSMDodge` returns on its first line and the physics sweep never runs for
  the monsters shipped before this existed; and the set's allowed-state whitelist must
  declare `DodgeState`, so no amount of tuning can make a barbol dodge. That is the same
  guarantee the whitelist already gives vendors against ever chasing anybody.
- **The roll is SPENT whether or not it passes.** Sensing runs every frame, so a cooldown
  started only on success would let a 35 % chance fire within two frames — the dial would
  control how MANY frames it took, not whether the shot lands. Committing the cooldown on the
  decision makes a failed roll mean "this one gets through", and makes the window the
  player's counterplay: a second shot inside it cannot be dodged.
- **The sidestep is PERPENDICULAR to the shot, never away from the shooter.** A projectile
  outruns a monster, so backing down the line of fire keeps the body in the shot's corridor
  and only delays the hit. Both flanks are probed with the same `LineOfSight` every other
  steering decision uses; both blocked returns zero and the monster stands still, exactly as
  `FSMRetreat` lets a cornered one turn and swing.
- **The burst is sized in DISTANCE, not seconds.** A duration authored beside a speed is two
  numbers that must agree — retune `chasingSpeed` and the sidestep silently doubles. The
  state divides `dodge_distance` by the speed it is actually travelling at, clamped to
  0.12–0.6 s so a slow monster cannot divide its way into a dodge that reads as a hang.
- **No invulnerability frames.** A dodge that cannot fail teaches the player that shooting
  mid-sidestep is wasted mana rather than that the shot needs leading.
- **`AttackState` dodges out of the WINDUP only, never after `_attacked`.** A monster that
  could cancel any frame of any swing would be untouchable at range and its telegraph would
  stop being worth reading.
- **A dodge is not a short flee, and the FSM says so.** A flee stops fighting: it runs away,
  suppresses re-acquisition for `regroup_seconds` and exits to Patrol. A dodge keeps the
  target, suppresses nothing and returns to Chase — or straight to Attack when the sidestep
  happened to end inside reach. `Monster_Dark` declares no `FleeState` at all.
- **`FSMDodge` holds the decision; the `ChangeState` call stays in `ChaseState.cs` and
  `AttackState.cs`.** `FSMBuiltInTransitionRegistryTests` takes its census by READING the
  state class files, so a transition moved one level down is a coded edge no test can see —
  the same trap `CastOriginContractTests` records.
- **These are the first monsters with `autoCast: 1` in the project's history.** The wiring
  (`EntitySetup.ConfigureMonsterAutoCast` → `NPCAutoCast` → `NPCCastState`) was authored,
  round-tripped and never exercised by shipped data. Four spells is the ceiling that matters:
  `SpellCaster` has four slots and a fifth key is registered in the book and never cast. The
  two spells here that author `spawnAtMouse` (`shadow_step`, `leap_slam`) degrade to
  facing-and-range for a non-`Player` caster, which is `SpellTargeting`'s documented
  behaviour and the right one — a monster has no pointer.
- **`hostile_slash_dark` is how they swing on top of `MeleeCombat`**, which already draws
  the same crescent every slash spell does, sized from the entity's own reach.
- **Reach is per class and the vampire's is not ~2.** She is baked at 256 px / PPU 96 =
  2.667 world units against the dwarf's 1.797, so a swing tuned for the rest of the roster
  visibly stops short of her own arms.
- **`fovDegrees` is 220, not the omniscient 360 default.** Something this fast that also sees
  behind itself has no answer except out-damaging it; the cone is what makes flanking work.
  `FSMPerception` still suspends the cone for two seconds after any hit, so stabbing one in
  the back does not make it permanently unable to turn round.

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
- **`by_eid` is keyed by an Entities-editor PLACEMENT id, not by monsterKey.** It beats `by_archetype`
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

## Monster sheet pipeline (wave13: barbol_muscle, barbol_young, red_dragon)

Three hostiles cut from side-view character sheets. The pipeline is the one
`build_monster_frames.py` already defined; wave13 replaced its SLICER and extended its
manifest, and everything about alignment, mirroring and scaling stayed where it was.

```text
staging/monsters/wave13_src/            the raw sheets (gitignored, OUTSIDE Assets)
slice_wave13_sheets.py                  keyed RGBA + <sheet>.slices.json
build_monster_frames.py                 Art/NPC/monsters/<key>/*.png + manifest
MonsterFramesImporter                   MonsterDefinition assets + MonsterCatalog
tools/atlas/wave13/wave13.config.json   what each sheet IS, and how big
```

- **The sheets had no alpha.** Both barbols shipped as RGB PNGs with the editor's
  transparency CHECKERBOARD baked into the pixels (measured 247–253 on all three channels).
  Every segmenter in this repo thresholds alpha, so those sheets segment as one object the
  size of the sheet with a grey chequerboard welded on. Keying is a BORDER FLOOD FILL, not a
  global threshold, because a barbol's teeth, the whites of its eyes and the specular on its
  belt bead are near-white and achromatic too — a "bright pixels are background" rule punches
  holes through exactly those, small enough to pass review and obvious in game.
- **ENCLOSED plate pockets are background as well, and missing them was the real bug.** The
  gap between an arm and a torso is plate that never touches the sheet border, so a
  border-only fill leaves it opaque and it lands as a solid white patch on the character's
  hip. Measured: **146 pockets across 39 of the 41 RGB sheets**, worst frame 547 white pixels.
  What separates a pocket from an eye white is not SIZE (real pockets ran 90–2263 px and
  highlights up to 175, so the ranges overlap) — it is that a plate is WIDE and survives an
  erosion where a tooth does not. After: **74 near-white pixels across every built frame**,
  worst frame 6. `Wave13MonsterRigTests` budgets 40 per frame so a returned pocket is red.
- **THE FRAMES ARE NOT ON A GRID.** They look like one and are not: on `barbol_muscle_idle`
  the figures are ~376 px wide on a 362 px nominal cell, so every figure overlaps its
  neighbours' and **148 components straddle a cut line**. Grid-slicing puts a slice of the
  next barbol into this barbol's frame. Connected components work because the figures overlap
  in BOUNDING BOX and never touch.
- **Three ways of finding the frames failed first, and each failed differently.** A
  column-gap projection reports 5 frames for a sheet that holds 6, because overlapping boxes
  leave no empty column. A periodicity score prefers 2 almost everywhere, because a layout
  with one cut line has one chance to score badly and one with seven has seven. And READING
  THE CONTACT SHEETS BY EYE got `barbol_heavy_attack` (8, read as 6) and `barbol_muscle_run`
  (8, read as 6) wrong at thumbnail size — both caught later by a full-resolution view. The
  config still declares a count per sheet, not as the source of truth but as a CHECK that
  stops the build when the segmentation disagrees with what a human counted.
- **`build_monster_frames.py` gained two things and kept its single ownership.** Slices may
  now carry an explicit `anchor_x` and `row`, so a sheet that is not on an even grid skips
  the cell-membership assertion instead of tripping it — the slicer fits an evenly spaced
  ladder through the measured figure centres, which is the quantity the cell centre stands in
  for, and the residual between a figure and its rung IS the motion the anchor exists to
  preserve. And it emits `attackVariants` / `castVariants` through the same `emit_cycle` the
  base states use, because a variant differs from a state only in which list it lands in.
- **CEIL the canvas extents, never round.** Once an anchor is a float, a frame can need a
  fraction of a pixel more room than a rounded extent reserves, and it lands as
  `could not broadcast (453,377) into (453,376)` on whichever frame sits furthest from its
  rung.
- **`scaleAdjust` is the SCALE_OVERRIDE escape hatch, and one sheet needed it.** `body_px` is
  the tallest body across a state, so a sheet whose tallest frame throws both arms overhead
  measures tall for its size and the whole animation renders too small. The tell is that the
  sheet's HEIGHT ratio and its AREA ratio against the same monster's own idle disagree —
  measured, they agree within 6 % on twelve of thirteen `barbol_muscle` sheets and by 26 % on
  `spellcast_5`, which is the one that raises both arms. Nothing else in the wave needed one.
- **Sheets are drawn at wildly different zooms and normalising is mandatory.** Measured on
  `barbol_muscle`: the idle body is 453 px and the run body is 250 px in source, a 1.8x
  spread, with `hit` at 664. Height and sqrt(area) agree on the ratio, which is what says the
  difference is zoom rather than pose.
- **Size is a PAIR: baked pixel height and PPU.** `Art/NPC/**` imports at PPU 64 with pivot
  (0.5, 0), so the authored height IS the world size: `barbol_young` 168 px = 2.63 units,
  `barbol_muscle` 240 px = 3.75, `red_dragon` 288 px = 4.50 (and 8.2 units long).
  `Wave13MonsterRigTests` asserts the COMPOSITION, which is the only thing that catches the
  vampire-style failure where the PNGs are rebuilt and the metas still hold the old PPU.
- **The raw sheets do not live in `Assets/`.** They were dropped into
  `Art/NPC/monsters/new/`, which `npc.spriteatlas` packs WHOLESALE — 44 sheets, **62.8 Mpx,
  fifteen atlas pages** of art nothing references. They are in `staging/monsters/wave13_src/`
  now (gitignored, outside `Assets/`), verified byte-identical before deletion. Art/NPC went
  from 130.3 Mpx to 100.6 Mpx while GAINING three fully animated monsters.
- **Extra attacks are variants, and a variant needs COMBAT DATA or it is just an animation.**
  `knight_red` shipped five visually distinct attacks that were mechanically identical. The
  Coloso's four are four different answers: `kick` is gated to 2.2 units, `charge_slam` only
  fires from 3–9 and ends holding its last frame, `smash` hits 1.35x with a 1.3x recovery,
  `hook` is the bread-and-butter. `MonsterFramesImporter` refreshes a variant's SHEETS by KEY
  and never touches those numbers — matching by index moves every multiplier onto its
  neighbour the first time a wave inserts a variant in the middle.
- **A cast variant is reached by SPELL KEY, so one that reserves no spell can never play.**
  A monster has no cast rotation to fall into (`NPCCastState.ResolveCastVariant` asks
  `VariantForSpell` and takes -1 otherwise), so `roar` is reserved for `war_cry` and
  `root_surge` for `thorn_burst`. `Wave13MonsterRigTests` fails an unreserved cast variant.
- **The dragon's `cast` points at its ATTACK sheet on purpose.** An empty `cast` falls back
  to WALK in `EntityAnimationBinder`, so a breath weapon would play the dragon walking. Its
  attack sheet IS the dragon rearing with its mouth open. Three sheets, and that is the most
  they can say.
- **Behaviour is matched to what each one has art for.** `barbol_muscle` → `Monster_Boss`
  (no flee: a colossus does not run) with `thorn_burst` + `war_cry`; `barbol_young` →
  `Monster_Caster` (it DOES flee below 25 %, which is right for a sapling) with `entangle` +
  `spore_cloud`; `red_dragon` → `Monster_Boss` with `desiredRange 6.5` so it holds a breath
  band instead of nuzzling, a 180° cone because a long head does not see past its own wings,
  and Fire resistance 0.25 with Burn immunity.

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

## Facial expressions in chat

A character's face changes with what it says. Gatita is the first with the art; the layer is
built so a second character needs drawings and nothing else.

```text
Art/**/<character>/facial/<any>_<expression>.png   the convention, per character
Valkur > Chat > Import Facial Expressions          -> NPCPersonaDefinition.faces
FacialExpression (Valkur.Data)                     the closed, shared vocabulary
FacialExpressionFallback.Chain                     what a missing drawing shows instead
ExpressionTag        [happy] ...                   what the model declares
ExpressionClassifier                               the floor under it, offline
ChatSystem.CurrentExpression                       the single owner
ChatUI.Portrait                                    the gutter down the left of the panel
```

- **The vocabulary is GLOBAL and the art is PER CHARACTER.** Nine values, extensible by
  appending — never renumbering, because the enum is the wire format between the model and
  the panel and a reordered one silently changes which face an unchanged prompt produces.
  Defining it from whatever one character happens to have drawn breaks the second character
  that arrives, so what lets four drawings answer nine values is `FacialExpressionFallback`,
  the same job `EntityAnimationBinder` does for an animation state with no frames: show the
  nearest thing that exists, in the wrong INTENSITY rather than the wrong EMOTION. Which is
  why `Angry` falls straight to `Neutral` and never through `Sad` — a smaller version of
  cross is blank-faced, and sad is a different claim about the character that the player
  would read as one. `Neutral` is 0 so `default` is a face every character has.
- **The face rides the TEXT channel as a leading `[tag]`, not a tool call.** The provider
  already gives the model one tool, `propose_trade`, and a tool means "I want to DO
  something" — a model given a second one returns turns that are a tool call and no words at
  all, which for an emote is backwards: the face is a property of the sentence, so it has to
  ride the channel the sentence is already on. The tag costs about three tokens and degrades
  to nothing. It is stripped IN THE PROVIDER, before the reply is returned, because
  `ChatSystem` records what comes back to memory and to the session log verbatim — a
  surviving tag would become part of what the character is remembered to have said, and then
  what the do-not-repeat check compares against. An unknown word in brackets is deliberately
  NOT stripped: a character may open with a bracketed aside, and swallowing it would eat
  words a human authored.
- **`ExpressionClassifier` is the floor, and the default provider is offline.** It is the
  mirror of `DialogueIntentClassifier` — that one reads what the PLAYER typed to choose a
  reply, this one reads what the CHARACTER said to choose a face — and it answers whenever
  there is no model and whenever a model skips its tag. Both share
  `DialogueIntentClassifier.Normalize` / `.ContainsAny` (opened from private to internal for
  it) rather than keeping a second normaliser, or one would accept an accented spelling and
  the other not, with nothing failing. Ordered first-match, and the ORDER is the design: the
  warm set is tested before the pensive one, because a question mark is the commonest
  character in friendly dialogue and scoring it makes almost every line pensive. Emoji are
  checked FIRST and before normalisation, which turns every non-alphanumeric run into a space
  and would erase them; they are the only signal that works whatever language the reply is in.
- **The one moment worth more than the other eight is `Thinking`.** `GenerateReply` is
  fire-and-forget and a remote call is seconds long, so before this the player typed and the
  panel went silent with nothing on screen saying anyone was listening. An offline provider
  completes its await synchronously, so the wait face never renders a frame for it and no
  branch on the provider is needed.
- **The portrait FLOATS in a gutter made by layout padding.** The panel is a
  `VerticalLayoutGroup` and it overwrites the RectTransform of every child it owns — the
  `LangButton` once came out 504x0 that way — so the portrait is `ignoreLayout` and anchored
  to the top-left exactly like the corner controls, and the space is reserved by widening the
  group's LEFT padding. Every existing row then shortens by itself, with no row re-parented
  and no second layout group. `PANEL_MIN_W` is deliberately NOT raised: a per-character
  minimum would be static mutable state on a class where Domain Reload is off, and it would
  clamp a size the player saved on a portrait-less NPC upward the moment they talked to
  Gatita. At the minimum width the conversation gives up the space instead, the same trade
  `SCROLL_MIN_H` makes for the trade row. The gutter does not exist at all for a character
  with no art — reserving it anyway puts an empty rectangle beside five of the six
  conversations in the game, which reads as a portrait that failed to load.
- **`NPCPersonaDefinition.portrait` was authored, inert, and its tooltip lied.** Zero readers
  in the whole project, while the tooltip claimed "the panel falls back to the NPC's own world
  sprite" — code that never existed. It is now the last link of the fallback chain, which is
  the one job it can hold without adding a second mechanism.
- **`friendshipScore` and `moods.triggersUp` / `moods.triggersDown` are STILL inert, on
  purpose.** Nothing in production writes the score (only tests), so `PersonaPromptBuilder`
  has always read 0; the triggers are imported and read by nobody. Expressions were
  deliberately NOT built on that — a face resting on a layer nothing drives is a face that
  never changes, and wiring it would have turned a bounded job into "also fix the relationship
  layer". A persistent mood is a different thing from the face on one utterance and does not
  belong on `ChatReply`.
- **`faces` / `face` / `faceparade` exist for the same reason `SpellType.AnimationProbe`
  does.** A drawing reachable only by saying the right thing to a language model is a drawing
  nobody ever checks: an author cannot confirm the import, cannot compare two expressions, and
  cannot tell "never chosen" from "missing". `faces` reports which expressions have art of
  their OWN versus which resolve through the chain, so a half-worked import is a line of text
  rather than an afternoon. The override is a HOLD, not a one-shot, because a conversation
  writes the face on every reply and would take it straight back from the author looking at it.
- **The nine drawings are aligned by SILHOUETTE cross-correlation, onto one shared canvas.**
  `tools/atlas/wave6/build_gatita_faces.py`. Not by the artist's grid — measured, the cells
  disagree by 29 px horizontally and 57 vertically and two faces run into their own cell edge
  — and not by the snout, whose pink mask picks up blush, ear interiors and the tongue and
  whose centroid wandered 36 px. What does hold is that the ears, crown and head outline are
  the same drawing in all nine, so the alpha masks lock: NCC 0.87-0.96 with unambiguous peaks
  and corrections of at most 23 px. One shared canvas because these swap IN PLACE in the same
  rect — nine crops trimmed to their own alpha make the head jump every time the expression
  changes. The source sheet lives in `staging/npc/`, not under `Assets/`.
- **The swap is a crossfade over two stacked Images, never a cut**, for the reason
  `WeaponSwapFlashFX` exists. And two expressions can share one drawing through the chain
  (laugh and happy on a character that only drew happy), so dissolving a sprite into itself is
  refused — it is a flicker with no cause the player can see.

## Conversation journal (Diario)

Every conversation is written down as it happens, one page per character per day, and read
back from the **Diario** button in the chat panel's left gutter. At the end of a day the page
is sealed and the character's verbatim memory is cleared, so the panel opens on a blank
transcript and they greet the player again.

```text
ChatJournalPage / ChatJournalEntry   the page on disk: one day, one character, verbatim
ChatJournalPageRef                   a day, identified from its FILE NAME alone
ChatJournalStore                     load / save / list / delete. No clock, no notion of today
ChatJournal                          the live half: which page is open, and what midnight does
ChatSystem.Journal.cs                the seams — open, record, seal, and the Escape owner
ChatUI.Journal.cs                    the overlay over the conversation
journal / journal npcs / journal <n> the console probe
persistentDataPath/chat/journals/<slug>/<yyyy-MM-dd>_d<NNNNN>.json
```

- **The journal is written PER LINE, never archived from `ephemeralHistory` at the end of the
  day.** That window holds twelve messages and drops the oldest, so most of a long
  conversation is already gone by the time there is a day to seal. It hangs off
  `RecordToMemoryAndLog`, the one seam every message passes through — the same seam that
  already feeds the memory record and the session log, which is what stops the three
  disagreeing about what was said.
- **Three records, three jobs, and they are not redundant.** `NPCMemory.ephemeralHistory` is
  the twelve-message window the character REASONS from and forgets; the journal is the
  player-facing archive of the day and keeps everything; `ChatSessionLogger` writes a
  timestamped `.log` that no feature reads and exists for diagnosis.
- **One file per DAY, and no index.** Retention is unbounded, so a single document per
  character would have to be rewritten in full on every line typed — a cost that grows with
  how long the save has run. A page is bounded by how much a person types in a day, and
  yesterday is immutable the moment it is sealed. The day list is built from FILE NAMES
  (`ChatJournalPageRef`) rather than from an index, because an index is a cache of what the
  directory already knows and nothing throws when the two disagree.
- **The day key is `ChatDayClock`'s, which means a page can be sealed and written again.**
  The in-game half of that key is not persisted and runs backwards across a Play-mode
  restart, so the same calendar day is routinely left and re-entered; `LoadOrCreatePage`
  returns the EXISTING page and clears its seal, or one day becomes several half-empty ones.
  Ordering is therefore `(calendar date, in-game day)` compared as a date and an int — never
  the filename string, and never the in-game counter alone.
- **What a day boundary does, in one place (`ChatJournal`): seal the page, CLEAR
  `ephemeralHistory`, keep `digest`, `friendshipScore` and `visitCount`.** Forgetting the
  words is not forgetting the person. `lastGreetedDayKey` is deliberately left alone — it is
  already keyed on the day and already answers "is a greeting due", and a second mechanism
  saying the same thing is two things that eventually disagree about which day it is.
- **An EMPTY `lastJournalDayKey` is not a boundary.** It means a character never spoken to,
  or a record migrated from before the journal existed — whose verbatim window no page holds.
  Sealing there would wipe the only copy of it. `NPCMemory` schema v3 sets it empty for
  exactly that reason and the first conversation adopts today, so the NEXT boundary is
  detectable.
- **Midnight mid-conversation is handled**, through `DayNightCycle.OnDayChanged`, subscribed
  only while a chat is open and guarded by a flag: it is a STATIC delegate on a class with
  Domain Reload off, so a handler added twice fires twice and one never removed keeps a
  destroyed `ChatSystem` alive for the session. The panel clears, the greeting is re-spoken
  (`GreetForNewDay`, shared with `OpenChat` so there is one answer to "has she said hello
  today"), and an open Diario holds the reader's place BY DAY — the new page arrives at the
  front of a newest-first list, so every index below it shifts.
- **An empty page is never written, and an emptied one is deleted.** A conversation opened
  and closed in silence is not a day, and a selector offering blank pages reads as a broken
  archive. Deleting matters as much as skipping: a page can be emptied after it was written.
- **Reset takes the journal with the memory.** A character who has never met you cannot be
  holding a diary of your conversations; `DiscardAll` also clears `lastJournalDayKey`, or the
  very next open seals a day that no longer exists.
- **`ChatJsonFile` is the one atomic-write/`.bak`-recovery/quarantine implementation**, shared
  by `NPCMemoryStore` and `ChatJournalStore`. Its temp name carries a GUID per write, not a
  fixed `.tmp` — the two stores really do write in the same frame, and a shared temp name is
  what makes overlapping writes collide (`WriteSerializedJsonAtomic` shipped that defect).
- **The overlay covers the CONVERSATION, not the panel.** The title row, the corner controls
  and the gutter stay visible: an overlay that swallows the close button and the resize grip
  is a window the player is stuck in, and `JOURNAL_TOP_INSET` is derived from
  `TITLE_ROW_HEIGHT` so it cannot go stale. It is opaque, because the transcript underneath
  would otherwise be legible through it.
- **Escape has ONE reader in the chat subsystem and it is `ChatSystem`.** The overlay raises
  `SetModalOverlay(true)` and listens for `OnOverlayDismissRequested`; two readers of that key
  in an undefined Update order is how a single press closes both the overlay and the panel
  behind it, or neither, depending on the frame. Enter closes the view for the same reason,
  and the input field is disabled while it is up — uGUI focus is not blocked by an image
  drawn over it, so otherwise the player types into a box they cannot see.
- **The gutter is a stack with one owner (`LayoutGutterColumn`).** Face, then Comerciar when
  the character trades, then Diario; Reiniciar is pinned to the foot. Both conditions really
  vary — five of six characters have no portrait, five of seven do not trade — and the
  children are `ignoreLayout`, so NOTHING arranges them and an overlap is silent. A hidden
  button is still SEATED but consumes no space, so switching it on later needs no re-layout.
  `PANEL_MIN_H` (259) is DERIVED from that column, and `ChatJournalPanelTests` states the
  constraint independently of the derivation.
- **`journal` / `journal npcs` / `journal <n>`** exist for the reason `faces` does: the
  archive is written by the message path, sealed by a clock and read by a panel, and each can
  fail in a way the others hide. Without the probe, "did midnight seal yesterday" is not
  answerable without waiting for midnight.

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
  **It passed for `StatKind.MeleeRange` while that stat reached no player damage at all**, and
  the reason is worth knowing because the test cannot be strengthened to catch it: it asks
  whether a stat reaches a COMPONENT, and MeleeRange genuinely did — `PlayerStats.Consumers`
  pushes it into `MeleeCombat.SetRange`. What it cannot ask is whether that component then
  does anything with it *for a player*. `MeleeCombat.TryAttack` is called from exactly one
  place, the monster FSM's `AttackState`; the player never melees through it (`PollCombatActions`
  owns the whole war surface and `MeleeCombat` reads no input). So for a player the chain
  terminated at `CombatRangeVisualizer`, a debug overlay — a stat with a display name, a
  description and 0.2 units per point in `StatCatalog`, moving a debug circle.
- **A slash's reach is the SPELL's, and a `SpellDefinition` knows nothing about who casts it.**
  All five playable classes swung `slash_regular` at its authored 2.6 units regardless of
  size, which was invisible while every character was 1.797 units tall and stopped being
  invisible when the vampire shipped at 2.698: the same arc is 1.40x the dwarf's body height
  and 0.96x hers, so one reads as an extended sweep and the other as a tight jab, from a number
  neither can influence. `MeleeReachScale.For(caster)` multiplies `hitRadius` by the caster's
  own MeleeRange against the 1.5 baseline every class authors — so it is exactly 1.0 for the
  whole shipped roster and moves only for a class tuned away from it, and it makes the stat
  above live for the first time (talents and equipment included, because `MeleeCombat.Range`
  holds the COMPOSED value).
  - **Monsters are excluded, and the shipped data makes that non-negotiable.** They already
    express per-creature reach the only way that was available — by authoring a separate
    slash asset (`hostile_slash` 2.4, `hostile_slash_giant` 5.0, `boss_barbol_slash` 6.5) —
    so scaling by their own `meleeRange` applies the same quantity twice. That range runs
    **0 to 7** across the catalogue: the seven vendors (0) would swing at radius ZERO and
    `barbol_boss` (7) would take its 6.5 u slash to **30.3 u**, most of the screen. The gate
    is the `Player` tag, the same test `SpellTargeting.ResolveGroundTarget` uses.
  - **Reach is quadratic in swept area**, so it is not a free dial: the vampire's 1.44x is
    2.07x the area. `MeleeReachScaleTests` refuses a class authored over twice the baseline
    for that reason, and refuses one BELOW it, which would shorten a reach the whole
    catalogue was tuned at.
  - **The scale is applied before the slash is spawned**, because `SlashAttack` derives its
    sweep from the radius it is handed — a late multiply would leave the drawn arc and the
    damaged arc at different sizes, which is the one failure this file already records for
    the legacy slash path. Pinned by a source scan, because a correct helper nobody calls is
    precisely the shape the bullet above describes.
  - **The factor is MEASURED, not the height ratio assumed.** Limb extension is pose-dependent
    and does not track height: measured on the shipped frames, the vampire's punch reaches
    1.176x the dwarf's and her kick 1.684x, against a height ratio of 1.451. The two
    approaches agree only in the mean (1.430), which is what makes 1.44 defensible — a single
    move would have argued for anything between 1.18 and 1.68.
- **Spells are earned now.** `EntitySetup` still registers the whole catalogue, then
  `PlayerProgression.SyncSpellBook` REPLACES the book with exactly what the character
  knows — measured live, 77 registered spells become 2 on a fresh dwarf. Replacement, not
  addition, because a respec has to take spells away. The Spells Editor lifts it through
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

## Crafting and professions

Five trades — cooking, blacksmith, mining, lumberjack and a generic `crafting` bucket — share
ONE recipe type, ONE service and ONE panel. Only cooking has recipes today; the other four are
declared so the tab, the level curve and the station vocabulary exist the moment their recipes
are written, and so that adding one is a data edit rather than a code change.

```text
tools/crafting/recipes/cooking.json        the authored recipe source
tools/crafting/build_crafting_manifest.py  -> generated/crafting_manifest.json
Valkur > Crafting > Import Crafting Content -> Data/Catalogs/Crafting/{Professions,Recipes}/*.asset
                                              Resources/Crafting/RecipeCatalog.asset
                                              Data/Catalogs/Items/{Material,Consumable}/Cook/*.asset
ProfessionDefinition / RecipeDefinition    the data (Valkur.Data)
RecipeCatalog                              professions + recipes, one asset, under Resources/
CraftingService                            Evaluate / TryCraft / MaxBatches — pure, no MonoBehaviour
PlayerProfessions                          per-trade level + xp, saved in ProgressionSaveData
CraftingStation                            IPlayerInteractable, per trade; empty profession = workbench
CraftingPanelUI                            the player's screen — HUD tray, or a station's badge
SkillsRuntimeEditor                        the 17th runtime editor (ESC -> Skills)
```

- **The BALANCE is derived, in Python, from one authored ingredient price table.** 25 ingredient
  rows produce all 36 dishes' value, healing, hunger, rarity, weight and xp, so retuning beef
  re-prices the asado, the arepa and the milanesa in one edit. The importer does NO arithmetic —
  that is what keeps the balance auditable from outside Unity.
- **The importer's overwrite rule is NOT the usual one.** The persona and tileset importers fill
  only empty fields because everything they carry is prose. Here the fields split: DERIVED
  numbers are always rewritten (a generator that cannot propagate its own retune is not a
  generator), PROSE is filled only when empty. The Skills editor owns EXCEPTIONS, the generator
  owns balance, and the editor says so on screen — otherwise an author retunes a level, runs the
  importer for an unrelated reason and silently loses it.
- **`ItemCategory` is DERIVED, never stored, and that is the whole taxonomy.** An ingredient is
  stackable with no consume effect, so it files as Material; a dish carries healing and hunger,
  so it files as Consumable. The manifest carries a `rawHunger` per ingredient and the importer
  deliberately DOES NOT write it: any non-zero hunger sends `ItemCategoryUtil.GetCategory` to
  Consumable, which would put potatoes in the Consumables tab beside the finished plates and
  collapse the split on its first import. Eating raw needs a separate consume path, not a value.
- **The atomicity is the point of `CraftingService`.** A craft that takes the ingredients and
  then fails to place the result has destroyed the player's materials. It cannot be avoided by
  checking for room FIRST — the removal is what frees the slots, so a pre-check refuses most
  legitimate crafts on a fullish bag. The order is remove, place, ROLL BACK on the remainder,
  and experience is granted LAST so a full bag cannot farm levels off a button that yields
  nothing. `CraftingServiceTests.FullBag_RollsBackEveryIngredient_AndYieldsNothing` reaches it
  with deep stacks, because taking 2 off a stack of 20 frees no slot.
- **An unknown trade reads as level 1, never 0.** Zero would refuse every recipe carrying the
  default `requiredLevel = 1` and make the whole system inert on a fresh character, silently —
  the authored-and-inert shape this file already records a dozen times.
- **Refusals carry a REASON and every shortfall, not a bool and not the first one.** Same
  argument `InteractionPromptInfo` makes: "you cannot make this" is not useful, and naming one
  missing ingredient at a time makes the player walk back after each trip. The order the reasons
  are tested in is a design decision — LEVEL first (the only refusal the player cannot fix by
  walking or gathering), then ingredients, then the station, then room.
- **A station belongs to ONE trade, and a station with NO profession serves every trade.** That
  second case is a deliberate general workbench, not an unconfigured asset. Stations join TWO
  registries and both are load-bearing: their own list answers "may this trade's harder recipes
  be made here", `InteractableRegistry` is what puts a badge over the forge. Missing the second
  is silent — the station works perfectly and nothing on screen says it is there.
- **The panel lists recipes the player CANNOT make, on purpose.** Its job is "here is what you
  could make IF", which is the only version of the screen that says what to go and collect. A
  list filtered to the affordable rows is empty exactly when it would be most useful.
- **The Skills editor shows malformed recipes; the player's panel hides them.** A broken row
  sends a player looking for an ingredient that does not exist, but the editor is the one screen
  where somebody can see the breakage and fix it.
- **Tagging an item `food` does NOT stock a vendor on its own.**
  `VendorConfigDefinition.inventorySeed` is a STATIC list, not a live filter over `itemType`, so
  a newly imported ingredient is tagged correctly, sits in the item catalog, and is absent from
  every shop until `Valkur > Chat > Wire Entities To Personas` is re-run — that rebuild clears
  and repopulates the seed from the tag, so it is safe to re-run and is the second half of a
  crafting import. Measured after doing so: Gatita's stock is 64 food slots.
- Eight cooking recipes need a station (six or more distinct ingredients): borscht,
  cazuela_chilena, hallaca, holubtsi, kjotsupa, locro, paella, varenyky.
- The art is cut by `tools/atlas/wave10/build_cook_items.py` from two sheets in
  `staging/items/cook/` — 5x5 ingredients, 6x6 dishes, one cuisine per row. Cells TOUCH, so an
  alpha projection finds only three of the four row gaps; segmentation is core-seed clustering
  plus nearest-core assignment, the same machinery as `wave7/build_spell_icons.py`. Six legacy
  dishes (`borsh_01`, `perogi_01`, `completo_chileno_01`, `paella_01`, `tortilla_spain_01`,
  `hakarl_01`) were retired by the importer — the new sheet draws the same six plates, and
  keeping both put two borschts in the bag with different art and only one of them craftable.
  `food_chicken` is deliberately kept: it is a distinct item the sheet does not draw.

## The economy: a faucet, a spread, and a cycle

Audited 2026-09-07 at **3.9/10** and rebuilt the same day to **6.6** — full findings, the
measured numbers and what is still open in `.github/ECONOMY_AUDIT_AND_ROADMAP.md`. The
finding that frames the rest: **the economy was not broken, it was not connected.** The
pipeline was well built and nothing filled it.

```text
CurrencyWallet          Gameplay/Vendors/    the purse. AddComponent-ed, so its own
                                             startingCoins was never authorable
CoinDropSpawner         World/Pickups/       THE one owner of "put coins on the ground"
DeathDropSystem         Combat/Damage/       TryDropCoins — the game's primary faucet
VendorEconomyService    Gameplay/Vendors/    the 7-step price pipeline
EconomyGroupDefinition  Data/Quests/         margins: per-item, then per-TYPE, then default
MarketCycle             Core/Economy/        pure (seed, day) -> phase + multipliers
MarketService           Gameplay/Vendors/    the live owner: seed, day counter, persistence
market / marketday / marketseed / coins      DevConsole, category "economy"
```

- **THERE WAS NO COIN FAUCET, AT ALL, AND EVERY NUMBER LOOKED FINE.** `DeathDropSystem`
  dropped inventory, a loot roll and an XP orb and not one line of currency; **0 of 25**
  monsters paid out and **1 of 25** even had a `lootTable`; `QuestDefinition` has no gold
  field and zero quests ship; and the wallet started at 0 because `startingCoins` is a
  `[SerializeField] private` on a component `EntitySetup` **`AddComponent`s** — the same
  unreachable-field defect as `ChatSystem._catalog`. The only income in the game was felling
  a tree for a coin a swing. Nothing failed, because a shop whose prices the player cannot
  reach looks exactly like a shop. The faucet is now `MonsterDefinition.coinReward`
  (`-1` pays nothing, `0` = heuristic `hp/40 + power/4` on the SCALED stats, `>0` explicit)
  — the same three-way contract `xpReward` uses, so 25 monsters started paying with no data
  edit. Measured: barbol 4, knight_red 6, dark_vampire 12, barbol_boss 52,
  barbol_gigante 252, against a knight_longsword at 120.
- **`coinReward` is deliberately NOT derived from `xpReward`.** That field is a DIFFICULTY
  knob — how much a kill is worth as progress — and tying wealth to it makes every XP retune
  a silent economy retune. Two rewards, two dials.
- **The coin gate must read the AUTHORED faction, never a derived allegiance.** The
  `EntityFaction` component answers "who does this fight", where `AlliedUnit` membership WINS
  over the string. Routing the loot and coin gates through it would make a charmed EVIL
  monster silently stop dropping both — a reward that vanishes on the enemies the player
  worked hardest for. If one call is wanted, it is `AuthoredFaction`, never `.Side`.
- **A `buyPrice` of 0 is not free, it is ONE COIN.** It falls through to the fallback
  heuristic (`stackable ? 1 : 10`), so the **66 of 236** items that shipped at zero — every
  ore and gem the mining profession produces, up to rarity 4 — were worth the same as a
  pebble, with the pipeline behaving perfectly. Priced now on a rarity ladder
  (**6 / 14 / 30 / 65 / 140** buy, sell at half). `experience_orb` stays at 0 and is
  whitelisted as not-merchandise, by name rather than by a rule, so adding one is a decision.
- **Three of the pipeline's seven steps were dead in shipped data, and each is invisible.**
  All five vendors carried `economyGroup: {fileID: 0}` (both margin steps resolve to exactly
  1 — nothing distinguishes "no group" from "a group whose margins are 1"), no vendor carried
  a price override, and **not one of the six call sites ever passed a negotiation discount**,
  so `NPCPersonaDefinition.discountLimits` was imported, serialized on every persona and read
  by nobody. Every counter in the world charged identically and every character haggled
  identically.
- **The discount was pointed the WRONG WAY on the sell side, and the two bugs hid each
  other.** `GetSellPrice` applied the same REDUCTION as `GetBuyPrice`, so a persona willing
  to come down 20 % would also have paid 20 % LESS for what the player brought in — the
  friendliest character in the world would have been the worst one to sell to. It survived
  only because nothing ever passed a non-zero discount, and **fixing either half alone would
  have shipped the other**. `ApplyPremium` is the sell-side twin of `ApplyDiscount`: same
  strength, same cap, pointed at the player's benefit.
- **Specialisation is `typeMargins`, not a whitelist.** The catalogue has six item types
  (`lumberjack` 77, `food` 64, `mineral` 64, `blacksmith` 18, `alchemy` 6, `magic` 4) and 64
  minerals, so "the smith deals fairly in ore" is ONE row per type and would be sixty-four in
  `itemMargins`. A whitelist says something different and worse — that the character REFUSES
  everything else, which turns a specialist into a wall. Resolution is most-specific-first:
  per-item, then per-type, then the group default. Specialist `1.00/1.00`, outside the trade
  `1.35/0.70`, so a rarity-4 crystal pays 70 at the smith and 49 at the cook.
- **The smith takes `mineral` as well as `blacksmith`, and that row is load-bearing.** The
  64 minerals are the entire output of the mining profession and NO vendor was their buyer,
  which is most of why nobody noticed they were priced at zero. A profession whose product no
  counter accepts at a fair rate is a profession with no economy.
- **THE CYCLE IS ENDOGENOUS AND DETERMINISTIC, AND THAT IS THE WHOLE DESIGN.**
  `MarketCycle.Resolve(seed, day)` is a pure function — no clock, no network, no
  `UnityEngine.Random` — giving four phases (Boom / Peak / Bust / Trough) over a seed-derived
  6-to-14-day cycle, capped at **±25 %**. That cap is the load-bearing number: under ~0.10
  nobody notices the layer exists, over ~0.35 a Trough stops meaning "a bad week to sell" and
  starts meaning "do not play today". A shaped cycle rather than per-day noise because a
  random walk teaches the player nothing — a Boom TELLS you a Peak is coming, which is what
  turns holding an ore stack into a decision instead of a coin flip.
- **Buy and sell move TOGETHER, never inverted.** A Trough where goods are cheap AND scrap
  pays well is a free lunch on a timer, which turns the layer from a market into an
  arbitrage clock.
- **Applied AFTER the margins and BEFORE the discount**, and both halves of that order are
  deliberate: after the margins so a boom lifts a specialist's fair price and a generalist's
  mark-up in the same proportion (folding it into the margin bites hardest where the vendor
  was already dear), before the discount because haggling is the last word — 20 % off what
  the thing costs TODAY. A per-vendor price override skips the margins but **not** the cycle,
  or the overridden items are the only stable prices in the world and an arbitrage against
  the cycle itself.
- **`MarketService` keeps its OWN day counter and does not read `DayNightCycle.DayCount`.**
  That counter is not persisted and runs backwards across a Play-mode restart — the chat
  journal already records it — so a market driven by it rewinds to Boom every launch and
  "sell at the Peak" becomes a session trick. It subscribes to the day-changed EVENT as a
  tick and saves its own total. The subscription is guarded (`_subscribed`) because
  `OnDayChanged` is a STATIC delegate with Domain Reload off: added twice means two market
  days per world day.
- **The market clock only moves FORWARD, and `marketday` refuses a negative rather than
  clamping it.** A rewindable clock lets a Peak be farmed by reloading, which is the single
  exploit the layer has to be immune to.
- **Seed 0 is the "nobody set one" sentinel and is refused everywhere.** It is what an unset
  integer deserialises to, so accepting it would make "no seed" and "somebody chose zero"
  indistinguishable, and every never-saved run would share one economy. `DeriveSeedFromRun`
  hashes the run id with an FNV walk — never `string.GetHashCode`, which .NET may vary
  between processes, and never `Random.Range`, which would move the phase under a player
  mid-run.
- **BITCOIN: the IDEA (cycles) shipped, the SOURCE (a live feed) was rejected, and the two
  separate cleanly.** A live external price is client-authoritative in a single-player game
  (a proxy or a system clock moves it, so nothing balanced against it means anything),
  destroys determinism (no replays, no balance tests, no reproducible bug reports, and the
  same save loads as a different game tomorrow), is a random walk on the day scale rather
  than a legible cycle, requires the network AND an offline path — i.e. two economies with
  one of them tested — and carries storefront and "elements of chance" regulatory risk. It
  also amplifies whatever is broken underneath, which is why the cycle had to land after the
  faucet and the prices or it would have been decoration. What an index CAN honestly decide
  is the **seed**: `marketseed <n> [source]` is the only door, it writes into the save, and
  nothing re-reads it while playing. Read once, cached, replayable — an index picks the
  FLAVOUR of a stretch of days, `MarketCycle` keeps the RULES.
- **The market rides the save METADATA bag** (`market.seed` / `market.day` /
  `market.seed_source`), not a typed field: world state rather than player state, two ints,
  and the bag is already this project's answer for run-level facts. No schema bump, and a
  save that predates the layer simply carries no keys — which `RestoreFrom` reads as a fresh
  market rather than a refusal. `RestoreMarket` runs BEFORE the `data.player == null` guard,
  because a malformed player block must not silently reseed the world's economy.
- **`VendorEconomyService` stays null-safe about the market on purpose.** Every EditMode
  price test, and any scene built before the cycle existed, must resolve exactly as before —
  a pipeline that shifts when an optional service is absent cannot be tested in isolation,
  and every margin fixture would silently be measuring the cycle instead.
- **Crafting is NOT arbitrable and that was already true.** All 36 cooking recipes are
  negative against buying their ingredients (`-3` churros to `-11` paella). What is still
  open is the other end: `beef`, `potato`, `onion` and `herbs` are produced by no harvest
  table, so cooking is a pure gold sink with no gathering route into it.
- **A VENDOR'S OWN PURSE IS A SUPPLY-AND-DEMAND MODEL WITH NO MARKET CODE IN IT.** Vendors
  used to hold infinite coin and finite stock that never came back, which is exactly
  backwards: the player could sell an unbounded quantity of anything forever — so the only
  real gold faucet was uncapped — while the shop itself ran dry and stayed dry.
  `VendorConfigDefinition.coinFloat` / `restockSeconds` plus `VendorNPC.Purse.cs` invert both.
  Four rules hold it up. **0 means "as before" for both fields and had to**: a key absent from
  an already-shipped `.asset` deserialises to 0, so reading that as an EMPTY purse would have
  made all five vendors refuse to buy anything the instant the field was added, silently.
  **Money circulates** — what the player spends goes INTO the purse, or the float only ever
  falls and a busy shop becomes permanently insolvent instead of busy. **The purse is debited
  BEFORE the item leaves the bag**, because the other order destroys the player's goods for a
  vendor who turns out to be broke. And **restocking is PROPORTIONAL**, so `restockSeconds`
  reads as "empty to full" whatever the shop's size — a fixed per-tick amount refills a
  four-slot mage and a seventy-seven-slot lumberjack at wildly different rates from one number.
- **A refusal has to SAY something, on both paths.** `ChatTradeBroker.QuoteSell` cuts the
  quantity to what the vendor can afford, mirroring the cut `QuoteBuy` already makes on the
  PLAYER's purse — "I can take two of those" is the answer a shopkeeper gives, and refusing
  five because they cannot cover all five is the same mistake in the other direction.
  `VendorShopUI` shows `ChatLanguage.VendorCannotAfford` as a toast, because a Sell button
  that silently does nothing reads as a broken button rather than as a vendor who is short.
- **TESTING A VENDOR IN EDITMODE MEASURES THE LEGACY PRICE PATH, NOT THE PIPELINE.**
  `AddComponent` does not run `Awake` there, so `VendorEconomyService.Instance` stays null
  however carefully a fixture builds one, and `VendorNPC.GetSellPrice` falls to its legacy
  branch — a flat `sellPriceMultiplier` of 0.5 instead of the margins. `VendorPurseTests`
  first shipped asserting a 50-coin price against a 25-coin reality and FAILED a correct
  implementation. Read the price off the vendor and count; never hard-code the arithmetic.
- **Still open, and the cycle is thinner without them:** the only sink is buying inventory,
  and gold still buys no POWER — talents and spells cost points, so the cycle moves numbers
  on a window the player has little reason to look at.

## Quests: nine objective kinds, ten shipped, and a turn-in nobody authors

Audited 2026-09-08 at **1.0/10** — `QuestDefinition`, `QuestManager`, `IObjective` and
`KillCountObjective` were written and tested, and there were **zero shipped quests, zero
production callers of `StartQuest`, zero instantiators of `QuestLogHUD`** and no giver.
Built the same day. Design, the ten quests and what was left out:
`.github/QUESTS_TEN_DND.md`.

```text
QuestDefinition / QuestCatalog   Data/Quests/            the blueprint and the index
ObjectiveBase                    Gameplay/Quests/Objectives/  counter + the ONE progress event
QuestManager (+ .Persistence)    Gameplay/Quests/        progress, rewards, the poll tick
QuestService                     Gameplay/Quests/        catalogue, offers, speaks the hook line
ChatUI.Quests.cs                 Gameplay/Chat/          the Misiones sheet in the gutter
QuestContentSeeder               Editor/Quests/          Valkur > Quests > Seed Quest Content
quest / quests                   DevConsole              category "quests"
Resources/Quests/QuestCatalog.asset                      the index the runtime loads
Data/Catalogs/Quests/*.asset                             the ten definitions
```

- **`Quest` used to hear ONE kind of objective, and the hole was total.** The aggregator
  duck-typed `KillCountObjective.OnProgressChanged` and re-checked completion from that
  handler — so a quest whose last objective was anything else went complete and
  `OnCompleted` **never fired**: it stayed in the active list forever and never paid out.
  Invisible while KillCount was the only kind in existence, which is exactly why it
  survived. Every objective now reports through `ObjectiveBase.Progressed`.
- **`IObjective` is deliberately NOT widened with that event.** It is what fixtures
  implement with three-line stubs; a new member breaks every one of them for no gain. The
  aggregator asks `is ObjectiveBase` and treats anything else as a passive counter.
- **EVENT vs POLLED is the objective taxonomy, and Collect had to be polled.**
  `GameEvents.OnItemPickedUp` carries the item's DISPLAY NAME (`WorldPickup` passes
  `itemDefinition.displayName`) rather than its id, and it does not fire at all for an item
  that arrived by crafting, by trade or as a reward. Counting the bag cannot miss a source
  and is the only formulation that can go DOWN when the player sells the ore. Same argument
  for `ReachLevel` (a player already at the level never levels again) and `EarnCoins` (a
  savings objective, not a lifetime-earnings one). `QuestManager` ticks them at 0.25 s.
- **`Survive` accumulates `deltaTime` rather than reading a wall clock**, because the vigil
  has to STOP while the player is dead — a stored start time would finish itself while they
  were a ghost. Dying does not reset it: that would make a long vigil a memory test.
- **The turn-in objective is GENERATED, never authored.** A quest with `turnInPersonaId`
  gets a `TalkObjective` appended, gated on every authored objective being complete.
  Authored by hand it would tick on the very conversation that HANDED THE QUEST OVER,
  because the giver and the turn-in are usually the same character.
- **An unmapped `ObjectiveKind` makes a quest EASIER, not broken.** `BuildObjective` logs
  and returns null, and the objective is silently dropped from the list.
  `QuestObjectiveKindTests` walks the enum against the dispatcher for exactly that.
- **`StartQuest` is permissive; `IsEligible` is the gate.** The console and the fixtures
  need to force a quest on without standing up a level-12 character with three cleared
  prerequisites, and folding the check in would make "give me that quest" untestable.
- **The restore seeds counters WITHOUT raising the progress event** — reporting progress
  during a restore re-runs the completion path and pays the rewards a second time. It also
  retired a reflection reach into `<Current>k__BackingField`, which was one auto-property
  rewrite away from silently restoring nothing.
- **`QuestSaveData` is FLATTENED.** JsonUtility refuses a jagged array, so the per-quest
  counters run end to end with a parallel list of run lengths. The pack/unpack pair lives in
  one file, because two copies of it is how one drifts by one and a quest comes back holding
  another quest's progress. A save with no quest document restores an empty log rather than
  warning — every save written before this layer is exactly that.
- **`Resources/Quests/` holds the CATALOGUE only.** `QuestService` is `AddComponent`-ed by
  the boot sequence and has no inspector slot, which is the `ChatSystem._catalog` defect
  verbatim; the ten definitions live in `Data/Catalogs/Quests/` and are pulled in by
  reference, so the build-everything folder carries the index and not the content.
- **`GameEvents` gained two events and both are raised at the point of NO RETURN.**
  `OnItemCrafted` fires after the result is in the bag and the profession xp is paid — the
  rollback path returns before it — and `OnNpcConversed` fires after the panel is seated and
  the greeting is in, so a listener that answers by speaking lands under the hello. It
  carries the personaId and not the display name: a rename must not unhook a quest.
- **Handing in does NOT complete the quest.** The Entregar button fires the same
  `OnNpcConversed` a conversation fires, and the generated turn-in objective notices. One
  completion path whether the player pressed a button or just walked up and talked; a second
  one would be a way to close a quest whose gate had not actually shut.
- **The Misiones button is CONDITIONAL, like Comerciar and unlike Diario**, and it is
  counted in `PANEL_MIN_H` whether or not it shows — the minimum has to hold the tallest
  gutter the panel can draw, and the gutter's children are `ignoreLayout`, so a button that
  no longer fits simply overlaps Reiniciar in silence. `PANEL_MIN_H` went 259 to 293 against
  a `PANEL_DEFAULT_H` of 312; a minimum above the default would drag every player's saved
  panel size up through the restore clamp.
- **The Diario stays the LAST child of the panel.** Both sheets cover the same rect and are
  mutually exclusive, so their order decides nothing on screen — but "nothing may be built
  after the overlay" is an invariant `ChatPortraitLayoutTests` pins, and one that moves
  whenever a view is added is not an invariant.
- **`QuestLogHUD` had no instantiator for the life of the project**, and its per-objective
  subscription watched `KillCountObjective` alone — so every other kind ticked with the
  panel still showing the numbers it drew when the quest was accepted. `HUDBootstrap`
  creates it now and it binds to `ObjectiveBase.Progressed`.
- **`ShippedQuestDataTests` reads the shipped ten off disk** and resolves every
  `monsterKey`, `itemId`, `recipeId`, `spellKey`, zone name and `personaId` against the real
  catalogues. Every one of those is a plain string nothing validates at author time, and all
  of them fail the same way: a counter that never moves, which looks exactly like a quest the
  player has not worked on yet.
- **`Valkur.Core.UI.HudLayout` owns the top-right column, and it exists because two
  assemblies cannot talk.** The minimap is `Valkur.UI`, the quest tracker is
  `Valkur.Gameplay`, and `Gameplay -> UI` is forbidden — so the two were laid out in
  different LANGUAGES (the minimap in fixed pixels, the tracker in screen fractions
  `0.72-0.99` x / `0.55-0.92` y) and could only agree by luck. They did not: measured live
  at 1600x800 the minimap covered **192x196 px, 29.4 % of the tracker**, over the corner
  where the quest names and their counters run, and won on sortingOrder 105 against 40.
  `BelowMinimapTop` is DERIVED from the minimap's own block, so moving the minimap moves
  what is under it instead of sliding it behind.
- **Raising the covered widget's sortingOrder is never the fix** — it only swaps which of
  the two is unreadable. Stacking them is the one arrangement where both are legible.
- **A HUD canvas that does not use `HudLayout.Reference*` is scaled against something
  else.** The tracker shipped on Unity's default 800x600 with `matchWidthOrHeight = 0`,
  which at 1600 wide is a scale factor of **2.0 against every other HUD canvas's 1.0** — its
  text rendered at double size and the two corners drifted apart on every resize.
- **Re-run the overlap sweep AFTER moving a panel, because moving one is putting it in
  somebody else's place.** Dropping the tracker below the minimap put it under
  `MusicPlayerHUD` (order 150, alpha 0.85, 35.1 % covered). And a constant cannot fix that
  one: the music widget is draggable and its geometry is persisted per machine in
  PlayerPrefs, so any reserve computed against where it sits today is wrong for a player
  who has moved it. The answer was to occupy LESS — the tracker sizes itself from its own
  text with `GetPreferredValues`, capped by the free band. Measured after, with two quests
  active: 0 px against both, right edges aligned at 1576.
- **The sweep itself has a trap worth naming.** Filtering canvases with
  `GetComponentInParent<Canvas>() != cv` silently drops every INACTIVE one — that method
  skips inactive objects and returns null — so the first sweep returned a long, plausible
  list with the minimap missing from it. The one thing being looked for was the one thing
  filtered out.
- **The giver used to FORGET YOU.** An accepted quest leaves `OffersFor` and does not reach
  `TurnInsReadyFor` until it is finished, so for the whole middle of a quest the character
  who sent the player showed an empty sheet AND the Misiones button disappeared — which
  reads as the errand never having been given. `QuestService.ActiveFrom` feeds an EN CURSO
  section with each objective and its counter, and keeps the button on screen.
- **A character must not say the same thing twice in one conversation.** The hook and
  completion lines are announced on open, and the Entregar button deliberately re-raises
  `OnNpcConversed` — one completion path — so the handler ran again and repeated the line
  verbatim into the transcript AND into the journal, which records what a character is
  remembered to have said. `_announcedThisConversation` is cleared on chat CLOSE and not on
  open: `OnChatOpened` and the first `OnNpcConversed` land in the same frame in an order
  nothing pins, and clearing on the wrong side of that wipes the entry just written.
- **Accepting says an ACKNOWLEDGEMENT, never the pitch again.** The hook is already on
  screen twice by then — spoken on open, printed on the card just pressed — so a third
  reading confirms nothing and reads as a button that only scrolled.
- **Completing a quest produced no pixel anywhere.** The rewards land in four separate
  systems and the only evidence was a line vanishing from the tracker, which is
  indistinguishable from a quest that silently failed to pay. `AnnounceCompletion` toasts
  the name and what it paid — through the TOAST and not the conversation, because a polled
  objective can close a quest with the player nowhere near anybody.
- **A quest card is sized from its text, and 78 px is a FLOOR.** It shipped as a fixed
  height holding a bold name, the character's hook (the shipped ones run two or three
  sentences) and a reward line at font 11 in ~434 px — Gatita's hook alone is six lines
  that way, so the paragraph the sheet exists to show was the part being ellipsised.

- **`Valkur.Core.UI.WorldMarkerBoard` is how the quest layer puts a dot on the map, and it
  exists because there was NO CHANNEL.** The minimap is `Valkur.UI`, the quest layer is
  `Valkur.Gameplay`, and neither may reference the other — so the one system that knows
  where the player should go and the one system that can draw a map could not be
  introduced. Navigation scored 0.5 for that reason and not for difficulty.
  `MinimapMarker` is a MonoBehaviour, so publishing through it was the same wall; the board
  carries plain structs from an assembly below both.
- **The board is keyed by CHANNEL although it has one publisher.** A board that replaced
  its whole contents would work today and silently erase the quest markers the first time
  anything else published — a marker that vanishes for no reason the player or the author
  can see.
- **Half the objective kinds deliberately produce NO marker.** Collect, Craft, CastSpell,
  Survive, ReachLevel and EarnCoins have no place; inventing one teaches the player to
  distrust every arrow. Talk resolves the LIVE NPC position (five of seven personas walk,
  so an authored coordinate points at where a vendor used to be — the
  `RegisterDynamic` rule), Reach resolves the zone centre and refuses `(0,0)` because that
  is a real place, and KillCount resolves the nearest LIVE monster or nothing.
- **The generated turn-in must be excluded from the objective sweep.** Measured live, it
  put TWO markers on one character — a big green plus with a small blue diamond drawn over
  the middle of it, muddying the one dot the player most needs. Identified by position, the
  same rule `QuestService.AuthoredWorkDone` uses, so the two cannot drift.
- **`QuestBadgeState` is compared by VALUE, so its order is behaviour.** The publisher keeps
  the most urgent state for a character who is several things at once. The first cut
  numbered it `Offer=1, TurnIn=2, InProgress=3`, so **InProgress beat TurnIn** — a
  character who owed the player a reward would have shown the dim "you are working on
  something" glyph, the one state the player can do nothing with. It is
  `InProgress < Offer < TurnIn` now and `QuestNavigationTests` pins it. Safe to renumber
  only because it is runtime-only and serialized nowhere.
- **The badge is DRIVEN, never self-polling**, and the pass must push `None` as well as the
  positive states. A pass that only touched the personas it found something for would leave
  the last exclamation mark hanging over a character the player already dealt with.
- **A turn-in line must name the CHARACTER, not the personaId.** It read "Vuelve a hablar
  con vendor_banker_abigail" in the tracker, in the sheet and on the minimap label — a
  database key shown to the player in the one sentence that tells them where to go.
- **`QuestService.Manager` resolves LAZILY.** Unity does not call `Awake` on a component
  added in Edit Mode, so a fixture got a null manager and every method returned its
  empty-guard answer — the tests would have passed while measuring nothing. The laziness
  also removes a real Play-Mode ordering hazard.
- **A level-gated quest is SHOWN, greyed, with its requirement.** Filtered out it is
  indistinguishable from one that does not exist, which removes the only reason to come
  back to that character.
- **Abandon takes two clicks and arming is EXCLUSIVE**, and it disarms when the sheet
  closes — coming back to a red "Seguro?" with no memory of pressing anything is one click
  from losing a quest. Before it existed, abandoning was console-only, so a quest accepted
  by mistake was permanent for anyone actually playing.
- **CONTENT GAP, measured on the loaded world: 6 personas alive, 7 givers in the
  catalogue.** `npc_barbol_brother_felipondor` is a persona and a `MonsterDefinition` and
  NO spawner instantiates him, so `q_cripta_colina` and `q_vigilia_altar` are unreachable
  in normal play. The UI handles it correctly — no offer, no badge, no marker — which is
  exactly why it is invisible.

- **DROPPING A QUEST RAISED NO EVENT AT ALL, AND THE TRACKER IS THE ONE LISTENER THAT
  CANNOT SURVIVE SILENCE.** `QuestManager` had two events, started and completed, and
  abandoning is neither — so `AbandonQuest` removed the entry and told nobody. The minimap
  and the head badges healed themselves within half a second because `QuestMarkerPublisher`
  re-scans the world on a timer; the corner tracker is event-driven and had nothing to
  re-scan from, so a quest dropped in the conversation panel stayed listed there with its
  counters live for the rest of the session. `OnQuestAbandoned` is its own event and must
  stay one: `QuestService` subscribes `AnnounceCompletion` to the completion event, so
  reusing that would toast the rewards of a quest nobody finished — and pay none of them.
  It fires AFTER the removal, because a listener asks the manager what is active the moment
  it is told.
- **The tracker is a WINDOW now** — drag, resize, minimize, close, and a two-click drop per
  row. What it replaced could do none of that: one TMP blob in a `raycastTarget = false`
  rectangle, so the panel could not be moved off something it covered, could not be put
  away, and could not offer the one action a list of errands invites.
- **A panel's PIVOT decides which corner its grip can live in**, which is why this one is
  TOP-LEFT pivoted while sitting in the top-right corner. A rect grows away from its pivot
  and never towards it, so a top-right pivot pins the right and top edges and leaves a grip
  able to pull only left and down — the one gesture nobody makes. Top-left pivot,
  `ResizeGripCorner.BottomRight`, and the default POSITION is computed from
  `HudLayout.ReferenceWidth - ScreenMargin - width` so it opens exactly where the old fixed
  panel sat and still moves when the minimap does.
- **Geometry is PlayerPrefs under `valkur.questlog.*`, written at the END of a gesture.**
  The editor workspace layer cannot take it — every entry point there is typed on
  `GameEditorManager.IGameEditor` and keyed on an `EditorName` a HUD panel does not have —
  so this follows `MusicPlayerHUD`'s precedent. The drag is recorded from `LateUpdate` by
  comparing against the last saved value, because `WindowDragHandler` writes
  `localPosition` directly and raises no event; a write per frame is a file write per frame.
- **MINIMIZE does not touch the remembered SIZE.** Collapsing is a view, not a resize, so
  expanding restores the height the player chose rather than a title bar's worth of it.
- **CLOSE is remembered, so it needs a way back, and there are three.** Accepting a quest
  reopens it (the player's own action, at the moment the panel is most useful), `questlog
  on` from the console, and the Quests editor. The first one is the load-bearing one:
  runtime editors are gated out of a player build, so without it one click on the X would
  be permanent — the exact defect `DraggablePanel.ShowCloseButton` shipped in the Controls
  editor, made worse by the reopen living somewhere a player cannot reach.
- **`TMP_Text.GetPreferredValues` THROWS on a component added in Edit Mode.** A
  `TextMeshProUGUI` picks up `TMP_Settings.defaultFontAsset` in its `Awake`, which Unity
  never calls there, so the label has no font and TMP dereferences it while sizing the
  material array — a `NullReferenceException` thrown from inside TMP with the caller's code
  nowhere in the message. Four `QuestLogHUDTests` went red on it the first time the tracker
  measured its own rows. Assign the default font first (in Play Mode that is only what Awake
  already did; in a fixture it makes the measurement REAL) and fall back to a floor when
  even that is null. `ChatUI.Quests` has the same call and survives only because no EditMode
  fixture reaches it.
- **The Quests editor (ESC → Misiones) edits the RUN, not an asset.** Unlike Death or
  Economy there is no SAVE and no `.asset` written: a quest log is save state. It lists all
  ten across four tabs (Activas / Ofrecidas / Bloqueadas / Hechas) with each one's live
  progress on the row, because the question it is opened to answer is usually "which of
  these is stuck" and an author should not have to click ten rows to find out.
- **Every authoring action goes through the SAME seam the game uses.**
  `ObjectiveBase.ForceProgress` is `SetCurrent` made public — it raises `Progressed`, so a
  quest driven to its target here completes, pays out and clears the log by the one path
  that does those things. `RestoreProgress` was the tempting alternative and is silent by
  design, so an editor built on it could fill every counter and leave the quest sitting
  there unfinished: a second definition of "complete" that disagrees with the first.
  `QuestManager.ForgetCompleted` is the one action with no gameplay counterpart and must
  stay that way — `_completed` is what stops a quest paying twice, so anything a player can
  press that empties it is a way to farm rewards.
- **It accepts through `StartQuest`, deliberately NOT `QuestService.TryAccept`.** TryAccept
  re-checks eligibility, which is right for a button a player presses and wrong for the one
  screen whose job is to reach a quest whose giver is not in the world (Felipondor's two).
  The eligibility answer is still SHOWN — the Bloqueadas tab and `DescribeLock` in the
  detail — so forcing one is a decision rather than an accident.
- **Its list is NOT virtualised, and that is the scale talking.** Ten rows against the Items
  editor's 180 items x 38 columns: virtualising this would be machinery guarding nothing.
- **It declares no input actions**, so no `OwnerEditor` exists in that folder and
  `ValkurInputActions` is untouched — every verb is a button, and undo/save/close come from
  the shared `EditorShared` map. Adding a hotkey would have meant an action in the asset AND
  a descriptor in the closed `InputActionCatalog`.

- **Still open:** Felipondor is not in the world (two quests unreachable), no sound on
  accept/progress/complete (the catalogue has no ids, so any call must be gated on
  `HasSfx`), the tracker still has no toggle KEY (it has a console verb, a window
  control and an editor button, but a new key would mean an action in `ValkurInputActions`
  AND a descriptor in the closed `InputActionCatalog`), no off-screen
  compass, no repeatable or branching quests (`Quest` is pure AND), no escort (needs follow
  AI), and the item reward ignores `AddItem`'s leftover so a full bag loses it — the
  correct pattern is already in `CraftingService.TryCraft`. Full UI/UX audit of the whole
  flow, scored 2.4 → 7.2: `.github/QUESTS_UX_AUDIT_2026-09-09.md`.

## Death, the spirit walk, and the guarantee that a death has an exit

Audited 2026-09-07 at **2.8/10** and rebuilt the same day to **8.1** — findings, the measured
numbers and what is still open in `.github/DEATH_SYSTEM_AUDIT.md`.

```text
DeathTuning              Data/Death/            the 30 decisions, in one asset the editor reaches
DeathSequenceController  Combat/Death/          the phase machine: Alive -> Dying -> Spirit -> Reviving
  .Rescue.cs                                    the two clocks that make a dead end impossible
ResurrectionAltarRegistry Combat/Death/         the single answer to "where can the player revive"
ResurrectionZone         World/Buildings/       one altar, polling its own padded footprint
DeathLitter              Combat/Death/          the previous death's drops, so the next one can sweep
DeathStateSave           Combat/Death/          the save's metadata flag + corpse position
SpiritAltarPathHighlighter Combat/Death/        both trails: yellow to the altar, red to the body
DeathRuntimeEditor       Editors/Death/         ESC -> Muerte
death / altars / rescue / corpseloot           DevConsole, category "death"
```

- **THE BUG WAS A TEMPLATE ID, AND BOTH HALVES WERE RIGHT.** The stone arch was placed in the
  shipped world — `lobby (693, 815)` — under building template **197**, while the binder looked
  for **249**. Both ids are the same sprite (`Buildings/portals/portal_stone_arch`) and differ
  only in `splitRatio`; the placed instance even overrides its ratio to 249's 0.195, so somebody
  placed it deliberately AS the altar. Nothing failed: the binder bound nothing, the trail drew
  nothing, `Revive()` was never called by anybody, and an altar stood in the world looking exactly
  right. Measured live: `ResurrectionZone count = 0`, `SpiritPathMarkers.childCount = 0`,
  phase `Spirit` with the player 64 units from their own corpse and the DevConsole the only way
  out. Same shape as `SPAWNER_COORDINATE_SPACE_DRIFT` and the same answer — a LIST rather than an
  id, and `ShippedDeathDataTests` asserting the shipped world against the shipped asset.
- **BEING AN ALTAR IS A PROPERTY OF THE BUILDING, and that is where it lives now.**
  `BuildingTemplateData.isResurrectionAltar` + `resurrectionAnchor` + `resurrectionRadius`, beside
  `hasDoor` and for the same reason: every placement of a wayside shrine is a shrine, so the fact
  belongs to the ART. A list of ids in a tuning asset was the wrong home twice over — it could not
  be seen from the building you were looking at, and a template id is not a fact about death.
  `ResurrectionAltarRegistry.IsAltar(template)` is the ONE predicate; nothing else may ask.
  - **The old list is OR-ed in, never ranked.** `DeathTuning.altarTemplateIds` survives as a
    legacy bridge so no already-shipped world loses its altar, and nothing writes to it any more.
    Ranking them would need a way to say "this template is NOT an altar despite the list", i.e. a
    third state on a bool — which is how a compatibility shim becomes a second model.
  - **Switching the flag OFF must clear the legacy id too.** They are OR-ed, so turning off one
    half leaves an altar that reports itself as off and goes on reviving people. The Death editor
    clears both; `ShippedDeathDataTests` asserts the shipped altars carry the FLAG rather than
    only the id, which is what says the migration actually happened instead of the bridge quietly
    staying the real model forever.
- **WHERE on the building is four answers, because a building is not a point.**
  `ResurrectionAnchor`: **Proximity** (any side, within `resurrectionRadius`), **Base** (the
  bottom band plus slack in FRONT), **Center** (the middle band), **Top** (the top band plus slack
  BEHIND). A wayside shrine is touched from in front, an arch is walked through, a floor sigil is
  stood on — one hard-coded rule is right for one of those and wrong for the others.
  - **`ResurrectionZoneGeometry` is PURE and static**, taking a `Rect` and returning a `bool`, so
    all four shapes are provable in EditMode with no scene. That matters more here than usual: a
    zone in the wrong place is indistinguishable, from inside the game, from a zone the player has
    not reached — the exact silence this whole subsystem was rebuilt out of.
  - **The rect's `yMin` is the GROUND LINE and `yMax` the top of the canopy**, which is what
    `BuildingObject.TryGetWorldRect` reports. So "Top" is the far side of an arch you walked
    under, not somewhere in the sky: the canopy is drawn OVER the player, and walking behind a
    building is walking up.
  - **Base, Center and Top are INSIDE the building, so a solid one can lock them out.** The
    collision grid decides where the player may stand and knows nothing about altars — an arch
    works because its opening is painted walkable, a house would not. Proximity is the anchor that
    is always reachable, which is why it is the default and why both authoring surfaces say so.
  - **The reach is the LARGER of the radius and the padding, never their sum.** Padding is the
    sideways slack every shape gets; the radius is Proximity's own. Adding them would mean an
    author nudging the padding to make the BANDS easier to hit had silently widened every
    proximity altar in the world with it.
  - **A band never collapses to a sliver** (`MinBandHeight`). On a one-tile prop a third of the
    height is a few pixels, and a player standing on a strip that thin reads as an altar that does
    not work. Overlapping bands on a tiny prop are the correct trade — the player gets revived.
  - **`ResurrectionZone.AnchorPoint` is the centre of the ZONE, not of the building.** The trail
    leads the player to that point and the zone is what revives them; aiming at the building's
    middle while the altar only accepts its base walks the spirit to a spot that does nothing,
    which is the same class of lie as a straight-line path through walls it cannot cross.
  - Authored from **ESC → Muerte → Altares** (four buttons per template) or from the console:
    `altar` reports the nearest building, `altar on|off [cerca|base|centro|arriba] [radio]` sets
    it. Bare `altar` REPORTS rather than toggles — a command that flips shipped catalogue data on
    a bare invocation is one an author runs by accident while looking for its syntax.
- **`altarTemplateIds` is a list because two templates over one sprite is a NORMAL state of the
  catalogue** (`CLAUDE.md` already records 1176 templates over 1174 sprites). Dropping either id
  re-opens the bug for whoever places the other one, and it would look right on screen.
- **The rescue is the one axis that cannot be wrong.** Every other setting can be badly tuned and
  still leave a playable game; a spirit that cannot reach an altar has no verb left. Two
  independent clocks, because "stuck" has two shapes: a world with NO altar is knowable
  immediately and rescued on a short clock, while an altar the player cannot REACH is
  indistinguishable from a player taking their time and waits out
  `spiritTimeLimitSeconds`. The no-altar clock does **not** run while the binder is still
  searching — `BuildingLoader` streams a world in over several frames, so a player dying in the
  first seconds would otherwise be rescued for a world whose altars had not spawned yet.
- **A rescue revives at a FRACTION of max HP and an altar revives whole.** Without that gap the
  altar stops being worth walking to and the safety net quietly becomes the intended path.
  `DeathTuningTests.Defaults_MakeTheAltarBetterThanTheRescue` pins it.
- **"The spirit passes through walls" and "the path is a straight line" are ONE decision in two
  fields.** The shipped combination was the incoherent one: a straight-line compass through
  geometry the ghost bounced off, i.e. a route that could not be walked.
  `Defaults_KeepTheSpiritAndItsPathCoherent` fails the pair, not either half.
- **Reloading used to undo the death entirely.** `GameStateCollector` refused to write a save
  while `hp <= 0` and `GameStateRestorer` bumped any `hp == 0` back to full, so the newest save
  was always a PRE-death one: die, quit, load, and the inventory, coins and XP were all back —
  every dial that makes dying expensive was opt-in for the player. `DeathStateSave` rides the
  save's METADATA bag (the market's seed and day already do), so there is no schema bump and a
  save that predates the layer carries no key, which reads as "was alive". Only the SPIRIT phase
  is persisted: Dying and Reviving are mid-coroutine states measured in tenths of a second, and
  restoring into one is a soft lock rather than a death.
- **The restore does NOT fire `OnPlayerDied`.** That event has four other subscribers and one of
  them is `PermadeathSaveCleanupSystem`, which would delete the very save that was just loaded.
  It reaches `SpiritWorldGrayscale.ForceApply()` directly instead.
- **A cheat that charges the player is a distinction that was authored and inert.**
  `ReviveRoutine` fires BOTH revive events on every path, with a comment promising the two exist
  to tell a real revive from an instant one — and nothing read them apart, so DevConsole
  `resurrect` cost 10 % of the player's XP. Both events still fire on every path (so every UI and
  visual listener behaves identically however the player got up) and the difference is carried by
  `DeathSequenceController.LastReviveKind`, read by the one system it actually changes.
- **The corpse compass hangs off `ActiveCorpse`, so `corpseLingerSeconds` at 0 kills it on the
  frame the player stands up** — the start of the one walk it exists for. `RetireCorpse` keeps
  the reference while the corpse lingers rather than nulling it, and the shipped default is 120 s.
- **`DeathLitter` sweeps on the NEXT DEATH, never on revive.** Sweeping at revive would delete the
  pile the player has just walked back to reach.
- **`KillPlayerForTesting` REFUSES while invincible instead of clearing the flag.**
  `SetInvincible` has three independent owners (god mode, the Spells editor, the shield) and
  writing false here would switch off whichever was holding it — a defect this project has
  already shipped twice. It returns the reason so the editor can put it on screen.
- **Three scans became one registry.** The highlighter ran `FindObjectsOfType<ResurrectionZone>`
  on every path rebuild, the grayscale swept every `BuildingObject` comparing a hard-coded id, and
  the binder polled the loader — and none of them could answer "there are no altars", which is
  the sentence the shipped build needed to say. `ResurrectionAltarRegistry` keeps a destroyed
  zone out and a DEACTIVATED one in (membership is not eligibility — the `AlliedUnit` rule) and
  snapshots rather than handing out its backing list.
- **The trail counters must be zeroed on the hide path.** `AltarTrailLength`/`CorpseTrailLength`
  are what the editor's live readout and the `death` command print, and the early-out skipped
  `Rebuild` entirely — so after a revive the readout reported a 70-tile corpse trail with nothing
  drawn. A diagnostic that outlives what it describes is worse than none, in a subsystem whose
  whole problem was that it failed silently.
- **The pulse reads each marker's OWN stored tint**, never the live colour: two trails share one
  pool, and multiplying back what the last pulse wrote ramps a trail to white in about a second.
  The old single-trail version got away with it by rebuilding from a constant.
- **`Object.Destroy` is an ERROR in Edit Mode**, and both the registry's rebind and the editor's
  body rebuild run there.

## The arranque: a sequence that is data, and a bar that cannot lie

Audited 2026-09-07 at **3.9/10** and rebuilt 2026-09-08 to **8.5** — findings, the measured
numbers and what is still open in `.github/LOADING_AUDIT_AND_ROADMAP.md`. **The boot went
from 11 286 ms to 3 681 ms in the same session**, and neither of the two findings that did
it came from reading code: they came from the probe this work built and from comparing two
consecutive boots.

```text
BootStep                 Core/Boot/            one entry of the sequence, as DATA
BootProgress             Core/Boot/            the bar's arithmetic. Pure, tested
BootTimeline             Core/Boot/            timings, failures, self-calibrating weights
RuntimeEditorPolicy      Core/Boot/            do the 17 authoring editors exist in this build
LoadingText              Core/Boot/            every player-visible loading string, one place
SceneTransitionManager   Core/                 THE one door into a scene load
SceneLoadRouter          UI/Loading/           installs the screen into that door, BeforeSceneLoad
GameplaySceneSetup.Sequence.cs                 the 64-step sequence
GameplaySceneSetup.Start                       the runner: measure, guard, decide when to yield
MenuAssetPreloader     UI/Loading/           pages the world's sprites while the menu is up
MenuPreloadManifest    Core/Boot/            which folders, as data, with a two-way test
LoadFrameBudget        Core/Boot/            "have I held this frame long enough?"
boot / boot all / boot fallos / boot weights / boot recalibrar   DevConsole, category "boot"
```

- **THE BAR CANNOT REACH 100 % BY COUNTING.** `SetupStepTotal = 53` described a sequence that
  had grown to **70**, so the bar filled at 75.7 % of the work and the last seventeen stages —
  301 buildings, the spawners, the audio, `SaveService.Load` — ran with it pinned full. Nothing
  failed, because `Mathf.Clamp01` silenced the overflow. The total is `steps.Count` now, and
  `BootProgress.Fraction` is capped at 0.99 until `Complete()`, which is only reached after the
  last step: 100 % is a statement that the game is ready, not a number arrived at by counting.
  Same family as `SpriteTintStack`'s hand-maintained `LAYER_COUNT`.
- **Weights are MEASURED, not declared.** `BootTimeline` writes each stage's milliseconds to
  `PlayerPrefs` and the next boot uses them as weights, so the bar is calibrated by the machine
  it runs on and a newly added stage self-corrects on the second launch. The declared numbers in
  the sequence are first-boot estimates only. PlayerPrefs is the right home: they are a
  measurement of THIS computer and losing them costs one uncalibrated boot.
- **A step declares whether it needs its own FRAME.** The old runner yielded once per step
  unconditionally — 70 frames, 1.17 s of pure waiting at 60 Hz before any work is counted — and
  that was accidentally load-bearing, because Unity runs `Start` on the frame after an object is
  enabled. `BootStep.Barrier` says which steps genuinely need it (24 of 64); the rest are
  collapsed by an 8 ms budget.
- **The order is a value a test can walk.** It carried six documented, load-bearing constraints
  and exactly one was checked, by a fixture running `IndexOf` over the source for two method
  names. `BootSequenceTests` builds the real sequence and asserts them. Note the trap that
  followed: three source-scanning fixtures went red because the CALL SITE moved and one because
  the new file's own doc comment NAMES the thing it scans for — repoint the fixture at the new
  owner and strip comments before scanning; never re-inline the call to satisfy a grep.
- **`BootstrapPipeline` / `IBootstrapStep` / `BootstrapProgress` are DELETED.** Authored,
  commented ("Phase 1 migrates the monolithic `RunSetupSequence` wholesale"), covered by a
  139-line fixture, and referenced by nothing in production — the pattern this file records a
  dozen times. `BootStep` does that job and runs.
- **Fifty of fifty-five steps had no `try`.** One throw killed the coroutine, the ready signal
  never arrived, and the watchdog faded out anyway. The runner owns one exception boundary for
  all 64, `BootTimeline` collects the failures, and the screen shows them with a way back to the
  menu instead of depositing the player in a half-built world.
- **The watchdog is a HEARTBEAT.** It was a flat 15 s from activation — clock time, not silence
  — so on a slower machine the screen faded out WHILE the boot ran. It rearms on every stage
  report; 12 s of silence warns on screen, 35 s concludes the boot is dead. Not "is this slow"
  but "is this dead".
- **A grace window measured in SECONDS is fooled by one frame, and this cost a live defect.**
  Every scene routes through the screen now, so it has to notice that the main menu has no boot
  sequence to narrate — and the first rule, "silent for 0.75 s means nothing to say", expired
  INSIDE the single enormous frame that activates the gameplay scene. Measured: the screen faded
  with the boot at **72.8 %**, which is the exact failure the rewrite exists to remove,
  reintroduced by the convenience. `ShouldConcludeNoPhase2` counts FRAMES as well as seconds — a
  three-second activation is still one frame — and it is a pure function so four tests can pin
  it. It was found by booting the game and reading the number, not by reading the code.
- **`SceneTransitionManager` is the one door — but ONE DOOR IS NOT ONE CURTAIN.** Four of the
  six scene transitions used to call `SceneManager.LoadScene` synchronously — pause > New Game,
  pause > Exit, a `ZonePortal` with a destination scene, and the first Bootstrap > MainMenu hop
  — re-running the whole boot behind a frozen frame. `LoadSceneHandler` is the relay (Core may
  not reference UI, same shape as `LoadingReporter`), `SceneLoadRouter` fills it at
  `BeforeSceneLoad` because the first transition in the game is `GameBootstrap.Start`, and a
  refusal falls back to the direct load rather than silently dropping a transition.
  **Routing everything and SHOWING everything are different decisions, and the first version
  conflated them**: with the screen shown for every transition, the player got a full loading
  screen — bar, tip, minimum display, fade — in front of a main menu that loads in a blink and
  reports nothing, and a second one the moment they pressed New Game. Two screens back to back
  at startup. `SceneLoadRouter.NeedsLoadingScreen` asks whether the target has a boot sequence
  behind it, reading `GameBootstrap.GameplaySceneName` rather than keeping a second copy of the
  name. The way back — gameplay to menu — stays on the direct path deliberately: it is a
  synchronous load either way, and a screen in front of one is a still frame with a bar on it.
- **The seventeen authoring editors are gated, and lifting their runtime side effects out came
  FIRST.** Roughly a third of the sequence built the development toolbox in every player.
  `RuntimeEditorPolicy` is a runtime check, not an `#if`, because several of them carry real
  runtime responsibilities: `TileEditorManager` is the sink for every zone's collision tags and
  `MapEditorManager` owns the world teardown every portal and door goes through, so neither is
  gated. The ones that ARE gated first had their side effects extracted —
  `RegisterItemCatalogForRuntime`, `EnsureItemDropService`, the spawner and monster catalog
  registrations — or gating them would have taken the item catalog and the drop pipeline down
  with the toolbox, in a build nobody runs in the Editor.
- **`boot` is the probe, and it answered the question the moment it existed.** Measured on the
  first live run: **6 749 ms — 60.1 % of an 11.23 s boot — in "Levantando los edificios"**, 301
  instances at ~22 ms each. Per-SUB-STAGE timing then split that open and **refuted the obvious
  suspicion in one run**: the cost is "Colocando los edificios" at **6 458 ms**, the 301
  `Instantiate` calls themselves, while the per-cell collider pass ("Enlazando sus colisiones")
  is **137 ms**. None of those numbers existed before, which is why every previous optimisation
  of the boot was a guess — and why the FIRST guess this time was wrong too.
- **The menu is idle time nobody measures, and that is where the first-touch cost belongs.**
  A first boot of the gameplay scene cost **5 649 ms** and a second one in the same session
  **3 115 ms**; the 2 534 ms difference is asset paging, paid once per launch. Touching the
  same two trees from the menu costs **1 315 ms** (`Buildings` 334 ms / 1 256 sprites, `Tiles`
  981 ms / 4 117) and took that first boot to **3 681 ms** — **75 % of the penalty recovered**,
  moved to where nobody is watching a bar. `MenuAssetPreloader` does it on a frame budget and
  **stands aside the moment a real load starts**, because half a preload is worth exactly what
  it paged. `MenuPreloadManifest` is a declared folder list because a built player cannot
  enumerate `Resources/` — the directory structure does not survive into `resources.assets` —
  and `MenuPreloadManifestTests` compares it against the disk IN BOTH DIRECTIONS, which is what
  keeps it from becoming another authored-and-inert table. A folder missing from the list is
  silent: the boot simply pays that first touch again.
- **Building the world behind the menu was rejected on its COST, not its size.** Loading the
  gameplay scene additively and running the save-independent steps there would move another
  ~1.4 s, and it is the obvious next idea. But the boot sequence does not merely load data, it
  STARTS SYSTEMS: spawners fire, NPCs walk, the day clock runs, autosave arms. A live world
  behind a menu needs the sequence split into build and activate phases and two scenes' worth
  of singletons arbitrated — for a gain smaller than paging assets, which instantiates nothing,
  ticks nothing, and costs nothing to abandon half-done.
- **A frame budget is not always fewer frames, and the first attempt was worse than what it
  replaced.** The loaders yielded on counts (60 buildings, one sixth of the zones) tuned when a
  building cost 22 ms. Replacing them with a 12 ms budget YIELDED MORE, because with the work
  now cheap a small budget simply trips more often. Measured warm: 12 ms gave 61 frames /
  3 237 ms, 250 ms gave 45 frames / 3 115 ms. And the number that settles where to optimise
  next: **the time spent outside the steps did not move at all** (1 838 against 1 832 ms). That
  cost is not the yields — it is work Unity does because the scene was just activated, and no
  amount of frame tuning reaches it. `LoadFrameBudget` is kept because it is measurably better
  and cannot rot the way a count does, not because it is where the time was.
- **`BootTimeline` counts FRAMES as well as milliseconds**, and that is what made the two
  paragraphs above answerable. `MillisecondsOutsideSteps` is the whole run minus the time inside
  steps: once the steps got cheap it was three quarters of the boot, which is the moment
  optimising a step stops being the right move.
- **`LoadingText` is where the player-visible strings live.** Five of the ten shipped tips named
  keys retired on 2026-09-05 ("Press F1-F12 to open the in-game editors", "F8 is the Tile
  editor", "Spells can be remapped from the F4 in-game editor" — F4 was never the spells
  editor), in English, in a game whose UI is in Spanish. `LoadingTipsTests` refuses any tip
  naming an F-key and any non-ASCII character, because this is the one file in the project with
  a demonstrated mojibake history.

## The FSM is two machines, and only one of them is authored

A monster's state graph has two owners and they do not overlap:

- **Authored** — `StreamingAssets/FSM/sets.json`, edited in the FSM editor. Supplies the initial state, the
  allowed-state vocabulary (which becomes `StateMachine.SetAllowedStates`) and a handful of
  transitions. `FleeState` and `AlertChaseState` are reachable ONLY from here: grep returns zero
  `new FleeState(` / `new AlertChaseState(` sites in the whole project.
- **Coded** — 24 `fsm.ChangeState(new X())` edges inside the state classes, plus the flinch and
  death edges raised by `StateMachine`'s event queue and the cast edge pushed by `NPCAutoCast`.
  These own every real decision: aggro acquisition, melee entry, de-aggro, leash, corpse timer.

Only the authored half was ever drawn, so the FSM editor showed three edges of a machine that has 27.
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

## Hostile AI: perception, standoff, the shout and the search

Audited 2026-09-06 at **5.2/10** and rebuilt the same day to **7.1** — findings, per-axis
scores and what is still open in `.github/HOSTILE_AI_AUDIT.md`. **Re-audited 2026-09-07 across
the WHOLE AI surface (21 axes, hostiles + allies + ambient + bosses + authoring + content) at
6.6/10: `.github/AI_AUDIT_2026-09-07.md`.** Two findings there outrank everything else and are
open: `NPCCastState` exits only when the spell leaves COOLDOWN, so an autocasting monster
stands motionless for the spell's whole cooldown (measured live: 9 of 31 monsters frozen, worst
10.3 s, three Dark Dwarfs at once mid-`leap_slam`); and the shipped world contains SEVEN placed
spawners, six of which are vendor respawns and the seventh an empty wave list — so in normal
play nothing drives any of this. The layer that came out of the first audit:

| Piece | Location | What it owns |
|---|---|---|
| `FSMPerception` | `Enemies/FSM/` | THE acquisition rule — range, field of view, line of sight, target viability — plus the aggro-suppression window |
| `FSMTargetMemory` | same file | Where the target was last SEEN, so losing it has somewhere to go |
| `FSMRetreat` | `Enemies/FSM/` | "Get away from that without running into a wall": a nine-heading fan probed with `LineOfSight` |
| `FSMPathFollower` | `Enemies/FSM/` | Repath timer + waypoint list + reach test, shared by Chase, AlertChase and Search |
| `AggroBroadcast` / `FSMAlert` | `Enemies/FSM/` | One monster spotting the player tells the monsters around it |
| `SearchState` | `Enemies/FSM/States/` | Walk to the last sighting, look around, give up |
| `AIDebugOverlay` + `ai` | `Enemies/`, `Bootstrap/` | `ai` reports every monster; `ai on` draws aggro ring, leash ring, view cone and current target |

- **Acquisition has ONE spelling now, and it had already drifted.** `IdleState` asked
  `IsBlocked`, `PatrolState` asked `IsClear`, and only one of them checked the target was
  alive. Everything goes through `FSMPerception.TryAcquire`, which is also why adding a cone
  and a memory was a few lines rather than an edit to every hostile state.
- **A field of view must never be a blind spot.** `fov_degrees` defaults to 360 (the
  historical behaviour), and whatever it is authored to, it is SUSPENDED for two seconds
  after any hit. Without that a melee attacker standing behind a monster is permanently
  unperceivable: the flinch resumes the state it interrupted, and the authored `t_any_alert`
  edge only fires when the attacker is BEYOND aggro range.
- **`desired_range` is what makes a caster a caster.** `ChaseState` used to close to
  `melee_range` unconditionally, so every caster walked into the player's face — and its own
  `NPCAutoCast.minDistance` gate then refused the spells the standoff exists for. Zero means
  melee and is every melee monster unchanged. A cornered standoff monster ATTACKS rather than
  grinding into the wall, because a caster that refuses to fight when it cannot give ground is
  a free kill that reads as a hang. `Monster_Caster`'s two `distance_to_player < 1.5` flee
  edges were retired with it: they turned a repositioning into a three-second rout.
- **Losing sight is now a mechanic.** Sight was checked on ACQUISITION only, so a committed
  monster tracked the player through a building forever. `ChaseState` records the last position
  it had line of sight on and, after `sight_memory_seconds`, hands over to `SearchState`. The
  window matters in both directions: dropping the target on the first blocked frame would make
  every pillar and every passing monster a hard reset.
- **The shout writes DATA, never a state.** `AggroBroadcast` puts an alert in the listener's
  context and the listener decides on its OWN tick, from `IdleState` or `PatrolState`. Changing
  another entity's machine from outside would run its `Enter` mid-swing, and — this is the
  half that bites — it would put a `ChangeState` in a file `FSMBuiltInTransitionRegistryTests`
  does not scan, so F12 would be missing an edge that really fires. Listeners with no
  `AlertChaseState` in their set are skipped through `StateMachine.IsStateAllowed`, which is
  what keeps vendors out of fights; the alert is CONSUMED on entry, or the monster re-takes it
  forever and never patrols again.
- **`FleeState` was worse than not fleeing.** It ran in a straight line with no geometry test
  (cornered monsters ground into walls for the whole window) and exited into `PatrolState`,
  which re-acquires on the next tick — so a monster under its flee threshold oscillated on the
  authored cooldown. It steers through `FSMRetreat` now, stands still when every escape is
  blocked, and sets a regroup window through `FSMPerception.SuppressAggro`.
- **`PathFinder` has a frame budget** (`maxSearchesPerFrame`, default 4) and a non-allocating
  `TryFindPath`. **"Refused this frame" and "no path exists" are different answers and must
  stay so**: a follower told "no path" walks straight at the target, which for a
  refused-because-busy search means walking into the wall the path was routing around. A
  refusal leaves the caller's list untouched and retries next tick. `maxPathLength` finally
  has a reader (it truncates), and the truncated case deliberately does NOT overwrite the last
  waypoint with the goal.
- **`FSMComponents` resolves the target once per frame**, with its `Health` and
  `PlayerSpiritState` cached per target. `AttackState` alone used to ask four times a frame.
- **Data:** all twelve hostiles author `aiTuning` (the block had never been serialised at
  all, so every knob ran on defaults); `mon1` was entirely zero while mapped to
  `Monster_Default`; `barbol_oscuro` had `speed: 10` against `chasingSpeed: 2.25`, and `speed`
  is the base `FleeState` multiplies. `ShippedMonsterDataSanityTests` pins the structural
  rules — positive HP, reach inside the aggro ring, patrol no faster than chase, a telegraph
  only where the windup clears `AttackState`'s floor, a standoff outside melee and inside
  aggro.
- **Test trap that cost a red suite:** both field-of-view fixtures set `Rigidbody2D.velocity`
  as the monster's facing BEFORE `fsm.Begin()`, and entering `IdleState` calls `StopMovement`
  — so the facing was zeroed and both tests measured the default east facing. Set it AFTER.
- **`FSMBuiltInTransitionRegistryTests` was RIGHT to go red here.** It asserted `FleeState` and
  `AlertChaseState` were reachable only from authored data, and the shout makes the second one
  reachable from code by design. It is two tests now: Flee keeps the old guarantee, and
  `AlertChaseState` must be entered from EXACTLY `IdleState` and `PatrolState`. Weakening it to
  "some code edges exist" would have thrown away the thing it is for.

## Targeting, difficulty and encounters

Re-audited 2026-09-07 across the whole AI surface at **6.6/10** and rebuilt the same day to
**8.0** — 21 axes, findings and measurements in `.github/AI_AUDIT_2026-09-07.md`. What came out
of it, beyond the hostile layer the earlier audit built:

| Piece | Location | What it owns |
|---|---|---|
| `EntityFaction` | `Enemies/` | Whose side an entity is on. DERIVES: `AlliedUnit` → player registry → `Player` tag → the authored `stats.faction` |
| `ThreatMemory` | `Enemies/` | Who has hurt this entity, decayed on a half-life, with a switch margin and a range limit |
| `EncounterDifficulty` | `Enemies/` | What level a spawn arrives at, from the encounter's PLACE bonus and the run's PROGRESS scale |
| `SpawnLevel` | `Enemies/` | That level, carried ON the spawned object so the two stat-scaling sites cannot disagree |
| `EngagementRing` | `Enemies/FSM/` | Which slot around the target each attacker steers at, so a pack surrounds instead of queueing |
| `NoiseEvents` | `Enemies/FSM/` | "Something loud happened here" — raises the same alert the aggro shout does |
| `Health.OnDamagedBy` | `Combat/Resources/` | The blow plus WHO threw it. Beside `OnDamaged`, which three listeners want without the attacker |

- **A COOLDOWN IS A RATE LIMIT ON THE ABILITY, NOT A DURATION FOR THE CASTER.**
  `NPCCastState` used to exit only at `CastPhase.Ready`, which `SpellCaster` reaches after
  `prepare + channel + cooldownDuration` — so an autocasting monster stood motionless for the
  spell's whole cooldown: 4 s for `void_lance`, 11 s for `thunderclap`, 12 s for `leap_slam`
  (a *movement* spell), **20 s for `war_cry`**. Measured live with 31 monsters: **9 in that
  state at velocity 0.00, worst 10.3 s and counting, three frozen simultaneously**. It exits on
  `Prepare`/`Channel` ending now, with a pose floor clamped to 0.30–0.85 s off the entity's own
  cast animation — because half the shipped hostile spells author `prepare: 0, channel: 0` and
  without a floor a monster fires spells out of nowhere while walking. After: **1–2 in the
  state, worst 0.83 s, 15 monsters moving**. It survived for the life of the project because it
  was latent in DATA, not code — `barbol_cyan` was the only autocaster ever shipped and it
  casts one 1.7 s spell.
- **`EntitySetup` sizes each auto-cast entry from the SPELL.** The period was a hard-coded 3 s
  for every monster and every spell, so a 20 s ability was attempted and refused six times out
  of seven while a 0.4 s one was held to a seventh of its rate. Period comes from
  `cooldownDuration`, the distance gate from `range`, jitter is a FRACTION of the period (0.5 s
  on a 20 s ability is no de-synchronisation at all), and slots stagger their opening cast.
- **Threat beats distance, and the roll is what makes it a decision.** `FactionTargeting` asked
  "who is nearest", recomputed every frame: damage bought no attention, two candidates at
  similar range made the target flip frame to frame, and nothing could be taunted or peeled.
  `ThreatMemory` decays on a HALF-LIFE rather than a timeout (a timeout has an edge, and the
  monster losing interest on it reads as a bug), needs a **1.35x margin** before it turns (two
  attackers doing similar damage otherwise make it spend the fight turning), forgets anything
  past `aggroRange * 2`, and re-checks the faction at the reader so friendly splash from an area
  spell can never turn a monster on its own side.
- **`stats.faction` is finally live, and it DERIVES rather than stores.** It was authored on all
  25 definitions and read by the loot roll, the respawn system and one `SetContext` nobody
  consumed. `EntityFaction.Side` asks `AlliedUnit` first, so a charmed `EVIL` monster fights for
  the player and reverts when the charm ends with nothing having to rewrite a field. **Neutral
  is on neither side of `AreEnemies`**: vendors are unfightable and unfighting BY CONSTRUCTION,
  where before it was true only because their `aggroRange` happened to be 0 — one edit in the
  Entities editor away from a shopkeeper hunting the player. An ally also stopped marching
  across town to kill the blacksmith, which `AlliedUnit.IsAllied` could never prevent because it
  answers "is this one of MY summons", not "is this a valid enemy".
- **The player is identified by TWO independent marks and an unauthored faction is HOSTILE.**
  Registry first, then the `Player` tag. Reading the player as Hostile makes
  `AreEnemies(monster, player)` false and switches off every hostile in the game — the loudest
  possible failure, arrived at in silence — and the first draft read only the tag, which every
  fixture in the suite lacks. Defaulting an unset faction to Neutral would have pacified half
  the suite the other way.
- **A pack SURROUNDS.** Every chaser steered at the target's centre, which with six attackers is
  a conga line down one bearing: only the front one can reach, and a player holds a doorway
  against a horde by standing still. `EngagementRing` hands each attacker an angular slot at the
  standoff. A LONE attacker is deliberately exempt (it would otherwise orbit its victim before
  engaging — the behaviour the ring exists to prevent, reintroduced by the fix for it), slots
  are STABLE while renewed (re-dealing makes the pack cross over whenever one dies), and it
  BIASES rather than commands: line of sight, the leash and pathfinding still decide, so the
  ring can never make a monster refuse to fight.
- **Perception is no longer purely visual.** `NoiseEvents` raises exactly the alert
  `AggroBroadcast` raises, so it inherits every property already thought through — the listener
  acts on its own tick, the alert is consumed, it expires, and a set with no `AlertChaseState`
  never receives one. Only the far side hears you (a monster swinging does not summon its own
  pack, which is what makes this a stealth lever rather than a global aggro button), and
  loudness is a RADIUS not a probability, because a roll makes the same action sometimes audible
  from the same spot and that is the one thing a player cannot learn. Emitted from
  `MeleeCombat.TryAttack` and `SpellCaster.ExecuteSpell` — the single seams both pass through.
- **`SearchState` was the one acquisition site that told nobody.** `IdleState` and `PatrolState`
  share `IdleState.OnAcquired` (which remembers the position AND shouts); Search re-acquired
  straight into Chase, so a monster that lost the player, hunted them down and caught them again
  was the only one in the pack that knew — and if it lost them a second time it searched the
  position from the FIRST sighting.
- **Difficulty travels with the SPAWN, never on the definition.** A `MonsterDefinition` is
  shared by every instance of that monster, so writing a level onto it to make one camp harder
  raises it for every monster of that kind in the world — and Unity keeps a ScriptableObject
  edited in Play Mode until the next domain reload, so the edit follows the author back into the
  Editor and rewrites the shipped balance. `SpawnLevel` is stamped on the object BEFORE
  `ConfigureMonster`, and both stat-scaling sites (`EntitySetup` and `FSMMonsterBrain`) ask
  `SpawnLevel.Of` — one value, two readers, no way to disagree.
- **Monster level growth is PROPORTIONAL, and that is arithmetic rather than taste.**
  `LevelStatCurve` is absolute (a flat `hpPerLevel`), which is right for the player, who has one
  hp scale. The bestiary spans **10 hp (`barbol_baby`) to 10,000 (`barbol_gigante`)**: a shared
  +18/level nearly triples the baby by level 2 and is a rounding error on the colossus, so no
  single authored number can serve the roster. `MonsterDefinition.levelHpGrowth` (0.12 on all 18
  hostiles) is a fraction of base hp per level; an authored `levelScaling` curve still WINS, so
  nothing already using one changed. Measured: baby 10→15 at L5, gigante 10,000→14,800,
  `dark_vampire` 240→442 at L8, vendors untouched.
- **Two knobs per encounter, and they are not substitutes.** `SpawnerTemplateData.levelBonus` is
  PLACE (this camp is deeper, whether the player is level 1 or 40); `scaleWithPlayerLevel` is
  PROGRESS (this region does not become furniture once outgrown). Place alone means the world
  stops mattering; progress alone means nowhere is more dangerous than anywhere else.
- **The world now contains fights.** It held SEVEN placed spawners — six vendor respawns and one
  `survival_10` whose wave list was **empty** — so in normal play nothing drove the AI at all
  and every monster in the audit had to be summoned from the console. Now seventeen: eight new
  templates, deliberately MIXED, because a caster behind two melee is the composition
  `desiredRange`, the shout, the threat table and the ring were all built for and no shipped
  encounter had. `survival_10` has ten waves that escalate in SHAPE as well as number — a ranged
  threat at 4, a dodger at 5, a standoff caster at 7, an elite at 10 — because a survival mode
  that only adds bodies asks the same question louder.
- **Placements are verified against real colliders, not against the collision grid.** The grid
  covers terrain; building colliders are baked at runtime by `WorldCollisionBaker`, so the only
  honest check is an `OverlapCircle` on World|Building in Play Mode. All ten new ones are clear;
  three PRE-EXISTING ones (`survival_10` and two vendor respawns) are not.
- **A boss phase can change the FIGHT, not just the spell list.** `BossDefinition.Phase` gained
  `adds`, `desiredRange`, `chaseSpeedMultiplier` and `dodgeChance`, all neutral at 0. Adds spawn
  through the ordinary `MonsterSpawner`, so a minion is a normal monster with a brain, a
  faction, a threat table and a ring slot — a boss-specific spawn path would be a second way to
  create a monster and would drift from the first. The chase multiplier is applied against the
  DEFINITION's speed, not the live context value, or two phase changes compound.
- **Ambient NPCs settle after dusk.** `StrollState` tightens its leash (2 → 0.7 units) and holds
  3–9 idle cycles instead of 1–5, so a villager who ambles the market square by day stays by her
  own door at night. The clock is POLLED once per bout, never subscribed: `DayNightCycle`'s
  phase change is a STATIC event with Domain Reload off, and a state object does not survive a
  detour through `DamageState`, so it has nowhere reliable to unsubscribe.
- **Two animation defects surfaced while fixing the above.** `DodgeState` was missing from
  `FSMMonsterBrain`'s state→anim switch, so it fell through to `_ => Idle` and the sidestep
  rendered as a monster standing still while sliding — the handler fires AFTER the state's own
  `Enter`, so the default silently overwrote the Chase pose `DodgeState` had just set. And
  monsters never resolved a cast variant (both the brain and `NPCCastState` called the two-arg
  `SetState`, which reuses an index that on a monster is -1 forever), so the 25 cast animations
  the Dark roster inherited from the player classes were unreachable.

## Spawners: a preset is a starting point, a placement owns its behaviour

Audited 2026-09-07 at **4.1/10** and rebuilt the same day to **7.6** — findings, the measured
duplication and what is still open in `.github/SPAWNER_AUDIT_AND_ROADMAP.md`. The finding
that frames the rest: **a placed spawner was not an object, it was a pointer.** Its whole
on-disk record was `template_id`/`zone`/`tile`/`id`, the runtime read every decision off a
shared ScriptableObject, and so the only way to express a new behaviour was a new asset.

```text
SpawnerTemplateData        Data/World/            the PRESET. What a fresh placement is born with
SpawnerInstanceConfig      Data/World/            THIS placement's own behaviour. The only thing read
SpawnerInstanceRecord      Gameplay/Spawners/     one parsed row, including rows that could not spawn
SpawnerInstanceSerializer  Gameplay/Spawners/     both directions of the file + the v1->v2 freeze
SpawnBrain                 Gameplay/Enemies/      what the ENCOUNTER says: FSM set, leash
SpawnerInstanceMigrationTool  Editor/Spawners/    Valkur > Spawners > Migrate Instances To v2
spawners [fragment]        DevConsole            the probe: state, roster, live count
```

- **Copy on place, exactly as `ParticleInstanceConfig` does it.** A placement takes a copy of
  its preset the moment it is made and is independent from then on, so editing a preset
  afterwards reaches the NEXT placement and none of the existing ones. Measured before the
  change: 6 vendor presets byte-identical but for one `entityId` string, 7 of 25 never placed
  (six of those named after their own parameter values, `barbol_auto_pair_3s_max2_restart10`),
  and a properties panel that wrote the shared asset — in Play Mode, which Unity keeps until
  the next domain reload, so the edit followed the author back into the Editor.
- **The old coupling is still reachable, deliberately: "Reapply preset → This / → All
  placements".** Copy-on-place makes a global retune impossible by accident; removing it
  entirely would make it impossible on purpose, which is a different mistake.
- **Serialized defaults are omitted against the CLASS default, never against the preset's
  value.** Omitting fields that merely agree with the preset would let a later preset edit
  reach back into every placement that happened to match — the coupling copy-on-place
  removes, coming back in through the file. This makes the field initializers on
  `SpawnerInstanceConfig` and `SpawnerTemplateData` **one contract**: if they drift, a value
  equal to the template default is written as nothing and read back as the CONFIG default,
  and the placement changes behaviour on its next load with nothing logged.
  `SpawnerInstanceConfigDefaultsTests` pins the pair AND that every preset field has a
  counterpart — the half that catches the next field, since a pair test cannot see a field
  nobody listed.
- **The roster is DEEP copied.** A shallow copy leaves every placement pointing at the
  preset's own `WaveDefinition` objects, so editing one camp's roster edits them all — copy-
  on-place defeated one level down, invisible until two placements of one preset exist.
- **The v1→v2 write-back is mandatory and emits what was READ.** In memory alone the freeze
  lasts one session, so retuning a preset and restarting would re-snapshot every un-migrated
  placement from the new values. It writes every row it parsed, including ones it refused to
  spawn (missing preset, unregistered zone) — which is also why it needs no anti-wipe guard:
  it cannot write fewer rows than it read. A row whose preset is missing stays v1 on purpose;
  freezing it against a blank preset would replace an author's data with defaults in silence.
  Editor + Play Mode only, plus a menu item so the SHIPPED file migrates as a reviewable diff
  rather than on whoever next hits Play.
- **A placement whose preset was deleted still loads.** Once a row owns its config it no
  longer needs the asset it came from; only a v1 row with no config and no preset is refused.
- **`ApplyConfig` re-seeds the state machine and does NOT withdraw what is already out.**
  Flipping Trigger from Proximity to Auto has to start the spawner or the control reads as
  broken; retracting live monsters because somebody widened a radius would be stranger to
  watch than a camp whose next wave differs from its last.
- **`SpawnBrain` sits between `by_eid` and `by_archetype`, and that position is the whole
  point.** Below `by_eid`, a hand-authored statement about one specific placement and the
  more specific claim. Above `by_archetype`, because otherwise a camp asking for
  `Monster_Caster` is silently overruled by the archetype mapping — which every shipped
  monster has, so the override would do nothing at all and would be the thirteenth
  authored-and-inert field in this subsystem. A set name no `sets.json` declares warns once
  and falls through rather than failing the spawn.
- **The brain is passed INTO `MonsterSpawner.SpawnEntity`, not applied to its return value.**
  `EntitySetup.ConfigureMonster` runs inside that call and is where `FSMMonsterBrain` builds
  the machine, so a stamp applied afterwards is read by nobody — the same ordering constraint
  `SpawnLevel` carries. The leash is published AFTER `PublishBehaviourTuning`, or a monster
  whose asset authors its own `leashRange` silently ignores the camp it was placed in; both
  values are plausible and only their order decides.
- **The twelve inert fields are deleted, not carried into v2** — `spawnerType`,
  `randomSpawnRadius`, `defendSpawn`, `defendLeash`, `visibleInGame`, `proximityInitialOnly`,
  `damageable`, `maxHp`, `flashOnHit`, `flashColor`, `flashDurationSeconds`,
  `hpResetOnEnter`, plus `SpawnerDefinition.cs`, a whole ScriptableObject class with zero code
  references and zero assets. Snapshotting twelve dead fields into every placement is what
  deferring the cleanup past the schema freeze would have cost. Two came back as real
  features: `proximityInitialOnly` is now `proximityRearms` with an actual re-arm, and the
  defend pair is now `defendLeashRadius` delivered through `SpawnBrain`.
- **The re-arm has hysteresis and runs OUTSIDE the state switch.** A camp that re-armed the
  instant the player crossed back out would fire again on the next step, so the re-arm ring is
  1.35x the fire ring; and a fired spawner is in Active/WaitClear/Done rather than Idle, so a
  re-arm folded into `UpdateIdle` could never run. It also refuses while the last wave is
  still alive, or walking out and back in stacks a second camp on the first.
- **`entityId` is picked from the monster catalogue, never typed.** It is a plain string that
  nothing validated, so a typo cost one console warning at spawn time and a camp that never
  filled — invisible until somebody walked to that clearing. `ShippedSpawnerRosterTests`
  covers the data already on disk, which no picker can reach.
- **Two rings, two colours.** The trigger ring is where the player sets the camp off; the
  SPAWN ring is where bodies land and therefore what must be clear of walls — it existed only
  in an `OnDrawGizmosSelected` under `UNITY_EDITOR`, needing the GameObject selected in the
  Hierarchy mid-play, while three shipped placements sit inside colliders. Two rings in one
  hue read as a single thick band at authoring zoom.
- **One write path.** The editor's save and the loader's migration both go through
  `SpawnerInstanceSerializer` and both write through `ISpawnerInstanceRepository`, whose write
  is atomic and map-slot aware. The editor used to hand-roll `File.WriteAllText` against a
  separately-resolved path — two writers, one file.
- **`persistent` is per-placement now, and it is a live exemption rather than a
  convenience.** It exempts every entity from `MonsterSpawner`'s despawn sweep; a world of
  exempt monsters is a leak with no error. `spawners` reports the count unasked and
  `ShippedSpawnerRosterTests` pins that only the vendor presets carry it.
- **When a coordinate disagrees with itself, ask CONSTANT OR RATIO before doing any
  archaeology.** An origin offset is a constant ADDED to both axes and shows as a fixed shift
  — Lobby is at (150, 50), which is what `SPAWNER_COORDINATE_SPACE_DRIFT` looked like. A
  RATIO is never an offset, and a ratio in this project is a pixels-per-unit. Measured here:
  `PlaceSpawner` minted instance ids from rounded WORLD coordinates while the file stores
  zone-relative tiles, and 6 of the 20 shipped rows disagree with their own `tile` — but they
  split into two causes. Two are off by one and two tiles (rounding); one sits at `[11, 20]`
  under an id saying `21_42`, almost exactly TWICE on both axes, which is a 16-versus-32
  pixel scale and not a drift. The fossil naming it was an unread
  `private const float PPU = 32f;` at the top of `SpawnerInstanceLoader`. That is the same
  shape as every Python-pixel sighting in this file — `wallWidth`, the totem's radius, the
  vortex's radius, `coneLength`, `arcane_flame`'s radius, `AuraExecutor`'s discarded divide —
  where the tell is always a constant compensating for units that stopped applying.

## The bars over an entity's head

Audited 2026-09-10 at **2.4/10** and rebuilt the same day to **8.6**; re-audited 2026-09-11 at
**4.6** after the colour art landed (two bugs and a regression, see "The second pass" below) and
iterated the same day to **8.7**. The numbers below are measured live at 1600x800 / ortho 5, i.e.
**80 screen pixels per world unit**, against the shipped dwarf whose body is
**1.219 x 1.859 world units (97.5 x 148.7 px)**.

```text
WorldBarGeometry     Core/UI/                 the texel grid + fill quantisation. Pure, testable
WorldBarPalette      Core/UI/                 one authored colour -> four hue-shifted tones; WCAG maths
WorldBarRank         Core/UI/                 Normal / Ally / Elite / Boss / Player
WorldBarStyle        Data/UI/                 the ~60 decisions, in Resources/UI/WorldBarStyle.asset
WorldBarArt          Gameplay/Combat/WorldUI/ ONE generated atlas + ONE material (+ the painted icons)
StatusGlyphs         Gameplay/Combat/WorldUI/ eight generated silhouettes, the fallback under the painted set
WorldBarRig (+.Layout) Gameplay/Combat/WorldUI/ the single owner: rows, feel, fade, sorting, bursts
WorldBarLine / WorldBarPip / WorldStatusIconRow / WorldBarJoints / WorldBarSparks   the pieces it draws
WorldHealthBar / WorldManaBar / WorldDashBar                DRIVERS. They own no pixels
SpiritTintExempt     Gameplay/Combat/Death/   the marker that keeps the spirit look off the readout
```

- **THE BARS WERE OFF THE PIXEL GRID, AT EVERY RESOLUTION, AND THAT IS PROVABLE RATHER THAN A
  MATTER OF TASTE.** `CameraSetup.SnapOrthoSize` solves `ortho = pixelHeight / (2 · 16 · N)`, so
  pixels-per-unit is always a multiple of 16 and **any length that is a multiple of 1/16 of a
  world unit lands on a whole screen pixel**. The shipped bars authored 0.8 / 0.1 / 0.07 / 0.04,
  none of which is: measured, the border padding was **3.2 px**, the mana and dash bars **5.6 px**
  tall, and the stack sat **158.3 px** above the feet. Every dimension is stated in TEXELS now
  (`WorldBarGeometry.Texels`), and the fill is additionally rounded to a whole pixel at draw time
  — the lerp stays continuous, the drawn width does not, which is what stops the leading edge
  boiling as the value moves.
- **A texel is not a pixel-perfect POSITION and this does not claim to be one.** The entity moves
  continuously, so the stack still lands between pixels exactly as every sprite in the game does.
  What the grid buys is that the bar's own parts keep whole-pixel sizes relative to each other and
  that the source texels map 1:1 instead of being resampled at 1.6 or 5.6 per texel.
- **Sizes go through `SpriteRenderer.size` in `Sliced` draw mode, never through
  `transform.localScale`.** The old bars were one 4x4 white texture scaled to 64 x 8 screen
  pixels — a sixteen-fold stretch of a four-pixel source, which is why no edge in them could be
  crisp and why a rounded corner was impossible. Sliced sizing leaves the border texels alone, so
  the chamfered corners and the fill's leading-edge highlight stay one texel wide at any width.
  `WorldBarRigTests.NoRendererIsSizedByItsScale` pins it in both directions (scale 1, and no
  renderer left in `Simple` draw mode, which cannot be sized without scaling).
- **Three components drew three bars and two of them carried private copies of the first one's
  geometry.** `WorldManaBar` and `WorldDashBar` each declared `healthBarMargin 0.12`,
  `healthBarH 0.1`, `dashBarH 0.07` and `dashGap 0.06` in order to stack above a bar they had no
  way to ask, so changing the health bar's height moved the health bar and left the other two
  hanging, silently. `WorldBarRig` is the single owner; the three components survive as DRIVERS
  that report a number, which is what kept `EntitySetup`, `AlliedSummonService`,
  `NPCRespawnSystem`, `UnconsciousState`, `InteractionPromptView` and `HarvestNodeBar` working
  unchanged.
- **Disabling a driver has to SAY so.** `UnconsciousState` puts a downed NPC's readout away by
  disabling the three bar components — which hid the bars only because each component owned its
  own children. With the drawing on a separate object that is a no-op, so `WorldHealthBar.OnDisable`
  calls `WorldBarRig.SetSuppressed(true)` explicitly.
- **The stack is TWO rows, not three, and the third one became a pip.** The dash bar was a
  full-width strip built from a one-segment loop, directly under a mana bar of the same width and
  shape, in cyan against blue — two adjacent rectangles differing only in hue are one rectangle to
  a glance and to a colour-blind player. A dash charge is a COUNT, so it is a square that fills
  from the floor, at the right end of the mana row. Shape now carries the meaning (thick bar /
  thin bar / pip / quarter marks) and colour only confirms it.
- **The one instant a dash readout exists for produced no pixel.** The old bar drew the charging
  ramp and then simply stopped moving. `WorldBarPip` flashes when the charge returns, and reads
  EMPTY during the lunge itself — `CanDash` is false while the dash is in flight, and a pip that
  stayed full through it would report the ability as available at the one moment it is not.
- **A blow and a heal were the same event.** The old bar subscribed to `OnHpChanged` alone, so it
  could see that a number had moved and never why: no chip, no flash, no shake, and a heal that
  looked exactly like a hit. `Health.OnDamaged` fires immediately before `OnHpChanged` inside
  `TakeDamage`, which makes the distinction free — `WorldBarChange.Damage` leaves the delayed
  chip, flashes the plate and knocks the rig sideways by a whole texel; `Heal` overshoots bright
  and RETIRES the chip.
- **The chip is snapped to the width currently DRAWN, never to the new target.** On a heal,
  setting it to the target leaves a bright band running ahead of the fill for the whole
  animation, which reads as a preview of health the creature does not have yet. Found by a red
  test on the first run, not by looking at it.
- **Two dark colours a percent apart are one colour, and this defect was reintroduced while
  fixing it.** The audit's own finding was that the old border (`0.9` black) was invisible against
  its own `0.12` background. The first rebuild shipped `plate 0.07` against `frame 0.05` and the
  live capture rendered a single black slab with no frame in it. Measured after separating them:
  along a horizontal line through the health row, **frame 0.01–0.02, plate 0.07–0.20, fill 0.64**,
  with the quarter mark reading 0.45 against the fill's 0.64. The plate's alpha was then raised
  to 0.97 for a related reason a screenshot does not show: at 0.90 the empty part of the bar
  measured anywhere between 0.04 and 0.20 depending on what was behind it, so "how much is
  missing" changed with the background.
- **Status effects were invisible, all eight of them.** Burn, Poison, Stun, Freeze, Slow, Root,
  Vulnerable and Marked are applied by shipped spells and monsters, and the only trace was the
  body tint — which `SpriteTintStack` multiplies together with the hit flash, the death fade and
  the transporter effect, so "it is burning" and "I just hit it" arrived on the same channel.
  `WorldStatusIconRow` is the readout. It is POLLED rather than subscribed: the apply/remove
  events cover membership perfectly and say nothing about the last second before a status ends,
  which is the half a player acts on.
- **Glyph downscaling is MAX-POOLED, not point-sampled.** The glyphs are authored 6x6 and the
  style ships 5: point sampling drops a whole source row and column, which is enough to take an
  arm off the snowflake or close the gap that makes the poison drop a drop. Max-pooling keeps
  every stroke and costs a little weight instead. Icons shipped at 6 first and measured taller
  than the entire two-row stack — the status row reading as the primary thing over the character
  is backwards.
- **One atlas, one material, generated in code.** Frame, plate, fill, quarter mark, pip and the
  eight glyphs share a single 128x64 `RGBA32` texture so the readout over every creature on
  screen stays batchable; a texture per piece is the obvious way to write it and costs a draw
  call per piece. It is generated rather than authored because every size comes FROM the style —
  changing a row's height regenerates art that fits it exactly instead of resampling art that
  does not. The shelf packer leaves a one-texel gutter, which is not tidiness: a stretched
  9-slice samples to its rect edge and would otherwise pull a neighbour's texel into its end cap.
- **The width follows the BODY.** One authored 0.8 served a roster spanning the dwarf's 1.219 u
  and the vampire's 2.667-unit height. `WidthTexelsForBody` rounds up to an EVEN texel count,
  because the fill is anchored on the left inner edge at `-innerWidth/2` and half of an odd count
  is half a texel — the one edge that must never move would be the one off the grid.
- **Measured on demand, never per frame.** Each animation frame is trimmed to its own alpha, so a
  per-frame measure makes the bar bob with the walk cycle. `WorldBarRig.Remeasure()` is called
  from `EntityAnimationBinder.ApplyLoadout`, the single seam a loadout swap passes through.
- **The sorting order is derived from the OWNER's Y**, the same formula `YSortEntity` uses for the
  body. The old bars used a constant 200..212 for every creature in the world, so the bar of a
  monster at the back drew over the bar of one in front. The rig's slots are DERIVED from what
  each piece claims (`WorldBarLine.SLOT_COUNT`, `WorldBarPip.SLOT_COUNT`, ...; the sum is
  `WorldBarRig.SORT_SPAN`, ~31 orders, i.e. ~0.3 world units of Y granularity) — two creatures
  closer than that on Y can interleave their bars, which is bounded and strictly better than no
  order at all. Deriving them closed a real collision: measured before it, the pip's frame and the
  mana fill both landed on +8. `WorldBarStackTests.EveryOrderTheRigClaims_FitsInsideItsDeclaredSpan`
  reads every renderer back; it is what found the sparks sitting a THOUSAND orders under the bars
  they were thrown off, because `SyncSorting` re-based every piece but them.
- **One visibility rule for every entity in the game, and the player's bars now fade.** The old
  behaviour was two rules: monsters hid at full health with a hard `SetActive`, the player never
  hid at all ("Python always shows player health bar") — which cost **34 screen pixels of
  permanent silhouette on a 149-pixel character**, repeating what `PlayerHUD` and `DashMeterHUD`
  already draw in the corner. A rig with nothing to report fades out after `idleFadeDelay` and
  comes straight back on the frame anything moves; while it is faded its root is deactivated, so
  a world of undamaged monsters costs nothing at all. Verified live: full health and no status →
  `alpha=0, rootActive=False`; one blow → `alpha=1` on the next frame with the fill already at
  135/200.
- **DEATH IS NOT THE SAME EVENT FOR A MONSTER AND FOR THE PLAYER.** The old bars had one rule —
  `if (_health.IsDead) show = false` — and the rig inherited it verbatim, so the player's readout
  vanished the moment they died. A dead monster IS a corpse and its bar is noise on something
  about to despawn; a dead player is a spirit walking to an altar against `spiritTimeLimitSeconds`,
  which is the one moment the run is actually in danger. Reported from play as "al morir las
  barras de la cabeza desaparecen", which is what a decision nobody wrote down looks like from
  the outside. The rank decides it now, and `WorldBarRigTests` pins both halves.
- **Verifying a death from outside the game is harder than it sounds, and the fixture is the
  proof rather than the capture.** Measured live: kill the player and eight seconds later the
  probe reports `phase=Alive, hp=200` — the rescue clock had already run, so the spirit window
  closed between two `execute_code` calls and never appeared in a sample. What settles it is the
  EditMode test, which drives `SetHealth(0, …)` and reads `AlphaTarget` in the same frame.
- **After a revive the bars fade anyway, and that is the idle rule working.** Full health, no
  status, nothing happening for `idleFadeDelay` — so they go. It reads like the death bug and is
  not one; the two were measured apart before either was touched.
- **`WorldBarStyle` lives under `Resources/` for the reason every other tuning asset in this
  project does**: every reader is `AddComponent`-ed by `EntitySetup` and has no inspector slot, so
  a `[SerializeField]` on it could never be filled — the `ChatSystem._catalog` defect. It also
  retired three separate sets of colour literals at three call sites (`EntitySetup` twice,
  `AlliedSummonService` once), which is what made the asset able to change anything.
- **Rank is DERIVED, and elite is not an authored flag.** Player comes from the tag, ally from
  `AlliedUnit` (set explicitly by `AlliedSummonService`, because the bar's `Awake` ran during
  `ConfigureMonster` before the component existed), boss from `MonsterDefinition.bossDefinition`,
  and elite from `SpawnLevel.Of(go, def) > def.level` — the one fact that already means "this one
  is harder than its kind", produced by `SpawnerTemplateData.levelBonus` and
  `scaleWithPlayerLevel` without anybody authoring it twice.
- **Testing traps this hit, all three already in this file.** Unity calls no `Awake` on a
  component added in Edit Mode, so a driver has to be started by reflection; `Time.deltaTime` is
  NOT a clock a fixture can rely on there — measured, one `LateUpdate` moved a fade by a frame's
  worth (dt ≈ 0.017 s), so a value under test drifts if the rig's own visibility pass runs over
  it — which is why the rig exposes `AlphaTarget` (the decision) beside `Alpha` (how far it has
  got), the test asserts on the first, and a test that needs a specific alpha ticks the PIECE
  directly instead of the rig. And `Object.Destroy` is an error in Edit Mode, so every fixture
  tears down with `DestroyImmediate`.

### The second pass: one instrument, not three strips

Re-audited 2026-09-11 after the colour sheet landed, at **4.6/10**: the generated look had been
8.6 and the painted frames took the rank, the pip states and the flashes away with them (`TintFor`
draws painted art white), the plate was off, and the first death corrupted every bar colour for the
rest of the session. Iterated the same day to **8.7**. What changed and why, each one measured:

- **ONE OUTLINE ROW IS SHARED BY THE TWO ROWS (`rowGapTexels = -1`).** With a one-texel gap the
  character's own helmet pixels showed through a slot in the middle of the readout and read as
  dirt; joined, the stack is one instrument. Joining exposed three transparent corner texels
  (every frame piece arrives with its own chamfers), which `WorldBarJoints` fills — a seam the
  width of the bar under the health outline, and the inner corner where the mana bar meets the
  pip. It draws UNDER the health frame so the low-health pulse still owns the whole ring.
- **The health row draws LAST** (`SORT_RESOURCE < SORT_PIP < SORT_JOINT < SORT_HEALTH`). The
  low-HP pulse lives in the health row's outline; drawn under the mana row it lost its whole top
  edge to the mana row's dark outline and read as a bracket.
- **The pip STANDS on the resource row's floor** rather than centred on it: it is taller than the
  thin row, and centred its lower half sat inside the health row. Stood on the shared outline it
  rises above the row as the stack's one ornament, and it is shaded like a stone rather than
  filled like a bar — shadow row, light row, one glint texel — because a flat square of the ready
  gold was the brightest thing over the character in the state it spends most of its life in.
  The glint appears only on a FULL charge; on a charging stone it would say "ready" in the corner
  of something that is not.
- **Every status glyph sits on the same dark TILE**, `iconTexels - 2` wide and as tall as the
  cell, chamfered, opaque, in the outline's own colour so each glyph's baked outline melts into
  it. The painted glyphs carry only an outline dilated from their own silhouette, so the row was
  three different shapes (a flame seven texels wide, a drop five). The tile's bottom row IS the
  remaining-time readout, draining in the status's OWN hue lifted toward white — as a separate
  cream line under each icon, three statuses made one near-continuous strip that was the
  brightest thing in the stack. The timer has a sorting slot of its own: at the glyph's order the
  dilated outline drew over it on two icons out of three (the burn timer vanished, the poison one
  kept two texels), which is invisible in the numbers and obvious in a 6x capture.
- **The plate is 0.88 alpha, not 0.74.** At 0.74 the pixels behind the bar showed through the
  empty part and the missing health read as texture. `WorldBarContrastTests` pins every fill at
  >=3:1 against the plate composited over four grounds (cobble, pale sand, foliage, dark stone);
  the pale ground is the hard case and the hostile red measures 3.2:1 there against 1.1:1 bare.
- **A two-row fill takes the SHADOW only.** The mana bar is two texels tall; a highlight there is
  half the bar and reads as a two-tone stripe rather than as light on a tube.
- **Low health is a RANK decision (`WorldBarStyle.LowFor`).** The player's own bar goes green to
  RED — amber was tried and read as the same family as the brass caps and the gold stone beside
  it; an ally's goes amber; a hostile's does not switch at all, and the heartbeat is the player's
  alone — on every monster near death it would be a field of pulsing rings reporting good news
  as an alarm. `WorldBarStackTests.TheHeartbeat_IsThePlayersAlone` pins it through
  `WorldBarRig.HeartbeatActive`, the DECISION, because the phase rides `Time.time` and can be at
  zero on the frame a test looks.
- **The ramp's leading edge is desaturated harder than its highlight** (`s * 0.30` against
  `s * 0.62`), or it is not the brightest tone for every hue: a blue's highlight leans further
  toward yellow — through cyan, the brightest part of the wheel — and came out lighter than its
  own edge. Found by `WorldBarPaletteTests`, not by looking. Fill motes peak at 0.7 alpha for a
  similar reason: caught in a still frame, a full-strength mote is a single white texel in the
  middle of the fill and reads as a dead pixel.
- **THE FIRST DEATH USED TO CORRUPT EVERY BAR COLOUR FOR THE REST OF THE SESSION.**
  `PlayerSpiritVisuals` tinted every renderer under the player (the bars included) and wrote the
  tint into a `MaterialPropertyBlock` `_Color`; the revive wrote the ORIGINAL colour into that
  same `_Color`, which left a colour frozen in the block that the shader multiplied into
  everything the renderer was coloured afterwards — measured, the low-health amber `(245,184,41)`
  rendered `(51,119,0)`, dark olive. Two fixes: `SpiritTintExempt` marks the rig's root and the
  spirit look skips that subtree, and the revive restores the renderer's ORIGINAL block verbatim
  (null when it was empty) instead of writing a colour into it. `WorldBarSpiritTests` pins both.
- **`Valkur/SpriteDesaturate` now takes the luminance of the texel TIMES the vertex colour.** The
  spirit world swaps every renderer's material onto it; ignoring `IN.color.rgb` drew the bars over
  a spirit as WHITE slabs (generated art is a white texture whose whole colour is
  `SpriteRenderer.color`) and, less visibly, drained the Dark roster's black-tinted twins to pale
  grey ghosts. The bars stay grey during the walk on purpose — the altar and its trail are meant
  to be the only colour in that scene — they are just legible now.
- **Verifying any of this live needs three tricks.** A hit-stop restores `Time.timeScale` to 1 a
  few milliseconds after a blow, so a slow-motion set BEFORE `TakeDamage` in the same call is
  undone before the capture — set it in the call after. The lobby altar sits under the spawn
  point, so a player killed there is revived by the altar within seconds and the spirit phase
  never appears in a sample — teleport ten units away first. And a source file edited between
  Unity's import and its compile leaves the test DLL one edit behind with a single
  `Import Error Code:(4) ... modification time` warning as the only tell; force a refresh and
  compare the DLL's write time against the source's before trusting a run.

### Hand-painted art for those bars

The bars generate their own art and always will; a painted sheet REPLACES it a piece at a time.

```text
WorldBarSheetLayout   Core/UI/            the rectangles. Read by all four users of them
WorldBarSkin          Data/UI/            one sprite slot per piece, on WorldBarStyle
WorldBarSkinImporter  Editor/UI/          Valkur > UI > Export Template / Import / Clear
Art/WorldBars/        world_bars.png      the sheet, 128x32 today (only the eight icons are painted)
                      world_bars_guide.png  the same sheet at 8x, boxed and numbered
                      world_bars_layout.md  the table, generated with them
```

- **The rectangles have four users and one owner.** The runtime generator draws into them, the
  template is written from them, the importer slices by them and the runtime check refuses a piece
  that disagrees with them. Four copies of a coordinate table is four chances for painted art to
  land one texel off, which is invisible in code and shows up as a sliver of the neighbouring
  piece welded to the end of a bar. They all read `WorldBarSheetLayout`.
- **Slots resolve INDIVIDUALLY.** A painted `frame_health` and twenty empty slots is a legitimate
  state: that piece switches over, the rest stay generated. An all-or-nothing skin would mean the
  first useful look at hand-painted art arrives only after all of it exists. The price is one extra
  draw call while both textures are in play, and it goes away when the sheet is complete.
- **`Art/UI/` is where this art must NOT live, and it took two separate mechanisms to learn that.**
  `ValkurAssetPostprocessor` forces `spritePixelsPerUnit = 100` and `FilterMode.Bilinear` on
  anything under that folder — screen UI wants both, world-space pixel art wants neither, and a
  sheet imported there is a sixth of its intended size with nothing logged. That one was expected
  and a dedicated branch answered it. **The second was not**: `ui.spriteatlas` packs the whole
  `Art/UI` tree and carries `filterMode: 1`, and **an atlas overrides the filter of the textures it
  packs**. Measured on the first exported sheet: PPU 16, rect 8x4, border (2,1,2,1) and pivot all
  correct, texture `sactx-71-2048x2048-Uncompressed-ui`, filter **Bilinear**, every bar and glyph
  soft. Four of the five properties were right, which is exactly why it needed measuring rather
  than reading. The sheet lives in `Art/WorldBars/`, which no atlas group packs.
- **Moving a folder does not unpack it.** A `SpriteAtlas` holds its packables as object references,
  and `AssetDatabase.MoveAsset` preserves GUIDs — so after the move the sprites still resolved to
  the old `ui` atlas page, still bilinear. What fixes it is repacking: `ImportAsset(ForceUpdate)` on
  the atlas and the sheet, then `SpriteAtlasUtility.PackAllAtlases`. Verify by reading
  `sprite.texture.name` back; a sprite whose texture is called `sactx-…` is in an atlas, whatever
  its own import settings say.
- **A painted piece is CHECKED on four properties and all four have failed here**: PPU, rect size,
  9-slice border and texture filter. It is refused with ONE warning and falls back to the generated
  piece, so the failure is a console line and unchanged art rather than a readout that is quietly
  wrong. The filter check exists only because the atlas defeated the other three.
- **The 9-slice border is not an import SETTING.** It is per-sprite data that lives in the
  `spritesheet` metadata and nowhere else, it survives no other route, and a frame without one
  stretches its own chamfered corners into wedges. `ApplyImportSettings` writes it from the layout;
  forgetting it is why the check asks.
- **A sprite's rect is not its body, and the roster is split on this.** The five wave3 characters
  ship frames trimmed to their own alpha, so the rect IS the body — the dwarf measures
  1.219 x 1.859. The legacy 8-direction art does not: the valkyrie is a 128 px SQUARE cell and
  measures **2.000 x 2.000** however much of it she fills, so a width read straight off the rect
  gave her a bar **1.875 units wide, 1.6x her drawn body**, against the dwarf's 1.03x. Nothing in
  the project separates the two pipelines and an atlas-packed texture is not readable, so the alpha
  extents cannot be measured at runtime. What is available is the PROPORTION:
  `maxWidthFractionOfHeight` (0.7) caps the width against the creature's own height, which leaves
  every trimmed character untouched (the dwarf is 0.656 wide over tall) and bounds the padded ones.
  It also catches a second case found the same hour — a hit-reaction frame is wider than an idle
  one, so an unlucky `Remeasure` could size the bar off a lunge.
- **The template refuses to overwrite itself.** Re-exporting to pick up a layout change rewrites
  the guide and the table and leaves `world_bars.png` alone, because that file is a day of somebody
  else's work the moment it stops being the generated art.
- **The guide's numbers are drawn OUTSIDE the boxes.** The first version put them in each box's
  top-left corner, where they covered a corner of every 5x5 glyph — on a reference image that is
  worse than useless, because it teaches the artist that those texels belong to the piece.
- **A painted piece may be in COLOUR, and then it must be drawn WHITE.** `SpriteRenderer.color`
  multiplies, so a `WorldBarStyle` colour is an instruction to the GENERATED (greyscale) art and is
  destructive over painted art: the shipped frame tint `(0.02, 0.02, 0.03)` turned the colour sheet
  into a black slab on the first live capture. `WorldBarArt.TintFor(id, styleColour)` answers white
  (keeping the style's alpha, which is the fade) for a painted piece and the style colour for a
  generated one; every row and the pip resolve their palette through it. The pieces the palette
  COLOURS — fills, plate, solid — therefore stay generated, which is why a skin is partial by design
  and a bar draws from at most two atlases. The generated fill keeps its body around 217 so its
  right-most column — the leading edge — has somewhere brighter to go.

### A test must not be able to write the authored world

`Valkur.Core.WorldDataWriteGuard` + `WorldDataWriteGuardTestHook` (Editor) + the floor in
`ShippedWorldDataIntegrityTests`. Full findings: `.github/incidents/PARTICLE_INSTANCES_TEST_POLLUTION.md`.

- **`!Application.isPlaying` IS THE WRONG QUESTION, in both directions.** It was the shape of both
  guards this project had, and it lets a **PlayMode test** through (`isPlaying` is true there) while
  refusing an **editor tool a human clicked** (`Valkur > Spawners > Migrate Instances To v2` writes
  shipped data outside Play Mode on purpose). The right question is "is a TEST RUN in progress",
  and only the test framework can answer it — `TestRunnerApi` callbacks registered from
  `[InitializeOnLoad]`.
- **An opt-in that is a bare `static bool` is a hole with a delay fuse.** A fixture arms it in
  `[SetUp]` and clears it in `[TearDown]`; the TearDown that lost 188 particle emitters deletes
  files, and deleting a StreamingAssets file Unity still holds mapped throws **Win32 1224**, which
  skipped the clearing line and left the door open for every test after it. `DisarmAll` now runs
  before EVERY test, and `AllowRealPathWrites` returns an `IDisposable` so it cannot leak inside a
  fixture either.
- **The guard sits at `WorldStreamingFileRepositoryBase.WriteFileAtomic`**, the one method all
  eleven JSON world repositories write through — before this, **one** of the eleven had any guard.
  A repository built with a `streamingRootOverride` is deliberately NOT refused: that is the
  correct way to isolate a test, and refusing it is the fastest route to somebody deleting the
  guard.
- **A refusal is a `LogError`**, so the run that would have destroyed the data goes red instead of
  going quiet. That is the whole point: 8,306 tests passed while the world was being emptied.
- **Forgetting to inject a store must be harmless.** Eight fixtures build a
  `ParticlesRuntimeEditor` and inject none; patching those eight does nothing about the ninth.
  `GetOrCreateStore` returns an in-memory store during a test run unless a scope is open, so the
  production path is reachable only by asking for it.
- **A guard can be bypassed; a floor cannot.** `ShippedWorldDataIntegrityTests` asserts the shipped
  counts and — sharper, because it needs no threshold — that **every placed particle names a preset
  the game ships**. The record left behind by the incident was `preset_id: "aura_smoke"`, a string
  that exists in exactly one place in the repository: the fixture that wrote it.
- **The anti-wipe guard in `ParticlesRuntimeEditor` compares against the count ON DISK**, so it is
  blind once a first bad write has landed — it refused nothing here because by then the file it was
  comparing against was already small. It is a second line, not a first.

## The minimap and the world map

Audited 2026-09-11 at **3.4/10** and rebuilt the same day — findings, per-axis scores and what
is still open in `.github/MINIMAP_BEAUTY_AUDIT_2026-09-11.md`. The finding that framed it: **it
was not a map, it was a radar over a black disc** — zero pixels of terrain, the "background tile
layer" its header promised was never ported from Python.

```text
MinimapStyle            Data/UI/            every look decision + the two shader refs (Resources/UI/)
MinimapWorldBaker       UI/HUD/Minimap/     the world's own art baked into a terrain atlas, by camera
MinimapFogMap           UI/HUD/Minimap/     explored mask per WORLD (outdoor / each interior), R8
MinimapScene            UI/HUD/Minimap/     every glyph source collected once per frame, by band
MinimapEntityClassifier UI/HUD/Minimap/     what an entity IS on the map, from the game's facts
MinimapView             UI/HUD/Minimap/     one map on screen: composite material + 3 quad layers
MinimapQuadGraphic      UI/HUD/Minimap/     one mesh of icon quads (glyphs, or additive FX)
MinimapFx               UI/HUD/Minimap/     the map's particles — events, never states
MinimapIconAtlas        UI/HUD/Minimap/     28 icons generated from SDFs, two tones
MinimapChromeSprites    UI/HUD/Minimap/     bevelled ring, shadow, plate, buttons
MinimapHUD (+UIBuilder, .Console)  UI/HUD/  the dial; owns the model, the bake and the world map
WorldMapPanel (+UIBuilder)  UI/HUD/Minimap/ N (Gameplay/OpenWorldMap): full map, pin, legend
Valkur/UI/MinimapComposite  Shaders/        terrain, ink, fog clouds + hatching, frontier, sonar
Valkur/UI/MinimapAdditive   Shaders/        glows and particles
minimap [rebake|reveal|fog clear|pin|zoom]  DevConsole probe
```

- **The terrain is baked by a CAMERA, not assembled from tile colours.** Buildings are sprites on
  sixteen sorting layers with a Y-sort, and a town map is mostly buildings; a camera renders
  exactly what the game sorts. One 32x32-unit chunk at 32 px/unit measured 3.7 ms. The bake is
  progressive and nearest-first within `bakeBudgetMs`, and dirty chunks come from
  `Tilemap.tilemapTileChanged`, a 1 Hz building-rect diff and a world-key change.
- **What the bake must not see is hidden by WHITELIST.** Entities, bars, particles and weather
  share the tiles' layer (all 32 physics layers are spent), so a culling mask cannot separate
  them. Everything except the grid's tilemap renderers and each building's sprite renderers gets
  `forceRenderingOff` for one synchronous `Camera.Render`, restored in a `finally`; the Global
  Light2D is held white at 1 and every other light at 0 for the same window.
- **`ScreenGradeFeature` skips any camera with a `targetTexture`.** Without that the vignette was
  graded into every baked chunk. It also stops grading the spell and particle preview cameras,
  which is what that feature's own comment already said it meant to do.
- **No Mask.** The composite shader antialiases its own circle; glyphs are kept inside by the
  projection (`MinimapProjection.ClampToRim` — RADIAL; the old square clamp threw corner dots
  outside the circle, where they vanished).
- **`MinimapQuadGraphic` needs `[RequireComponent(typeof(CanvasRenderer))]`.** Without it the
  first build drew no glyph at all — a `MaskableGraphic` added with `AddComponent` gets no
  CanvasRenderer on its own, and nothing logs.
- **Fog is per WORLD, not per zone.** The old code cleared it on every `OnZoneChanged`, and
  zones are 50x50 tiles edge to edge. A reveal writes a byte RAMP outside its radius, so the
  bilinear sample is a soft frontier. It persists per RUN (`Saves/<run>/minimap_fog.minimap`,
  merged on load so it can never un-explore), only in Play Mode, only into a run folder that
  already exists, and never with a `.json` extension (the save listing enumerates those).
- **The fog decides what may be drawn** (`MinimapReveal`): creatures only inside sight, places
  once explored, quest marks / the pin / the corpse / allies always. The first build drew every
  vendor over unexplored ink.
- **Draw order is meaning.** `MinimapScene` collects into four bands (ground, entity, landmark,
  quest) and concatenates them — stable, no sort delegate. The old dial drew the quest board
  BEFORE the vendor squares, so every quest in town sat hidden under its giver.
- **Entity kind comes from the game, not the dot type.** Every NPC was registered as a Monster,
  so the six vendors were red enemy dots; `EntitySetup.ConfigureMonster` now registers neutrals
  as `NPC`, and the classifier reads `EntityFaction`, `AlliedUnit`, `VendorNPC`, the bar rig's
  rank and `Health` directly anyway.
- **Particles are events** (`MinimapFx`): reveal motes on new ground, a ring when a quest mark
  appears, rim sparks FROM the attacker's bearing when the player is hit, a sweep round the ring
  on a new zone, a burst on reaching the pin, a gold burst from the player when an errand is
  completed, altar rings on becoming a spirit, and weather
  (rain streaks / snow) at the density the world's weather is rendering. The sonar ping is in
  the shader. Night motes are the only loop, and they carry no meaning.
- **The world map is an overlay, not a pause.** The backdrop is a raycast target (the combat poll
  skips presses over UI) and Escape is claimed through `EscapeOwnership` so closing the map does
  not open the General Editor. Clicking sets `MinimapWaypoint`; right click clears it.

## The player panel (bottom-left HUD)

Audited 2026-09-11 at **3.4/10** and rebuilt the same day to **8.9** — findings, per-axis
scores, the measured result and what is open in `.github/PLAYER_HUD_BEAUTY_AUDIT_2026-09-11.md`.

```text
PlayerHudStyle        Data/UI/                 every decision, Resources/UI/PlayerHudStyle.asset
PlayerHUD (+.Binding, +.Motes)  UI/HUD/PlayerPanel/  builds, binds, ticks. The only MonoBehaviour
HudArt                UI/HUD/PlayerPanel/      ONE generated point-filtered atlas + 3 one-off bakes
HudPixelFont / HudPixelText                    3x5 and 5x7 bitmap faces with a baked outline
HudBar                                         frame, chip, heal preview, fill, notches, heartbeat
HudPortrait / HudTextureBaker                  head crop from the east idle frame; icon minify
HudAbilitySlot / HudDashPip / HudStatusRow / HudMedallion / HudTooltip / HudFloatText
HudMoteLayer                                   event particles: one Graphic, pooled pixel quads
HudEdgeVignette                                the red screen edge (blows + low health)
Shaders/UIHudFx.shader                         UI/Default + blend mode + saturation + flash
```

- **The panel has its own whole-pixel space.** It is authored in TEXELS and drawn at
  `HudPixelScaleFor(screen)` screen pixels per texel — the CanvasScaler's own geometric-mean
  rule, ROUNDED (1600x800 = 2, 1080p = 3, 4K = 5). A `Pixels` child counter-scales by
  `scale / canvas.scaleFactor` and the corner is placed on a whole screen pixel. Drawn straight
  into the HUD canvas (factor 1.27 at 1080p) every frame edge and glyph was resampled. The outer
  RectTransform stays in canvas units, which is what the combo badge stacks against — and it
  moves with the resolution, so `HUDManager` re-stacks the badge on `PlayerHUD.GeometryChanged`
  instead of placing it once at build.
- **Every rect in that space is bottom-left, integer, via `HudRect`.** A centre pivot on an odd
  width puts everything under it half a texel off, which at scale 3 is a smeared glyph.
  `PlayerHudTests.EveryRectInThePixelSpace_SitsOnWholeTexels` walks the hierarchy.
- **The three mouse slots show what the BUTTONS cast.** The old row read `SpellCaster` slots
  0-2 under the labels 1/2/3; slot 0 is LEFT CLICK, 1-2 are empty for the player, the number
  keys cast the spell BOOK, and the cooldown ring read the slot clock a mouse cast never sets.
  The slots take their keys from `PlayerController.PrimarySpellKeyNow` / `SecondarySpellKey` /
  `MiddleSpellKey` (the cast code uses the same constants), read the BOOK cooldown, and draw the
  button from the action's live binding.
- **`SpellCaster.OnCastRefusedForMana`** exists for the panel: a refused cast used to be silent.
  It fires on every attempt (a held primary re-tries every frame), so the listener throttles.
- **Spell icons are 1024x1024, bilinear, no mipmaps, in a packed non-readable atlas.** Drawn into
  a 32-pixel slot they alias. `HudTextureBaker.Icon` halves them on the GPU (each halving at
  texel centres is a 2x2 box filter) down to the slot's pixel size and reads back once.
  The portrait bake does the same read-back at 1:1 to find the silhouette, because the character
  frames are FullRect sprites whose mesh says nothing about where the head is. Both refuse on a
  null graphics device and fall back to the raw sprite.
- **Motes answer events only** (blow, heal, spend, cooldown back, dash back, level) and are not a
  `ParticleSystem`: in an overlay canvas that would not sort with the Images around it. They are
  pooled quads of one Graphic on the additive HudFx material, positions snapped to texels.
- **Level 0 is the model's real starting level** (`XpRequiredForLevel(1)` is 100), so a fresh
  character's medallion reads 0 — not a bug. The bug was the old badge ignoring
  `Experience.OnStateChanged`, so a restored save at level 3 read the boot level until the next
  level-up; the panel listens to every Experience event including `OnXpLost`.
- **A heal must collapse the chip onto the fill, never push it ahead.** A chip in front of a
  growing fill is drawn pale over exactly the span the heal preview owns and reads as damage.
  Found by a red test on the first run.
- **Assert what is DRAWN, not what is modelled.** The first build's chip was correct in the
  model for the whole life of a blow and never on screen: `DrawWidths` re-evaluated the chip's
  visibility only when the chip's OWN width changed, and after a blow the chip keeps its width
  while the fill drops under it. The EditMode test asserted `ChipActive` and passed; a live
  capture showed a bare recess. `HudBar.ChipDrawn` reads the image.
- **In a linear-colour project, "96 % opaque" is not opaque.** Alpha blends in linear space, so a
  4 % gap lets ~18 % of a bright surface through once encoded for the screen — measured on the
  first tooltip, which showed the green health bar through its card. Every surface of the panel
  that sits over something is authored at alpha 1.
- **A ScriptableObject asset keeps the defaults it was CREATED with.** Changing an initializer in
  `PlayerHudStyle.cs` changes nothing for `Resources/UI/PlayerHudStyle.asset`, which serialised
  every field at creation. Re-sync the asset (copy a fresh instance's JSON over it, keeping the
  shader) whenever a default is meant to ship.
- **Photograph a transient at `Time.timeScale` ~0.005, in the SAME `execute_code` call that
  triggers it.** The panel runs on game time for exactly this reason (the pause menu stops it
  too). Round trips through the MCP bridge take seconds with the Editor unfocused, so a chip that
  holds 0.3 s and drains in 0.45 s is gone by the next call.
- **Screen-space overlay captures need `ScreenCapture.CaptureScreenshot`.** The MCP screenshot
  tool renders through the main camera and silently omits every overlay canvas — the capture
  looks like a game with no HUD at all.

## The action bar (bottom-centre): one face per posture

Audited 2026-09-11 at **1.9/10** and rebuilt the same day to **8.2** — findings, per-axis
scores, the measured result and what is open in `.github/SPELL_BAR_BEAUTY_AUDIT_2026-09-11.md`.
The finding that framed it: **the old bar was not ugly, it was untrue** — 24 of 24 printed keys
were wrong, 8 of its 12 top slots could hold nothing, the 4 that could were cast by no key, and a
click on any slot fired damage in Peace.

```text
SpellBarHUD (+.Faces, .Verbs, .Motes)  UI/HUD/SpellBar/     faces, the flip, motes, clicks
SpellBarModel                          UI/HUD/SpellBar/     which slots each face carries (pure)
SpellBarArt                            UI/HUD/SpellBar/     9 verb glyphs + the frame with the posture gem
HudSlotVerb + HudAbilitySlot.SetVerb   UI/HUD/PlayerPanel/  ONE slot for spells and non-spell actions
SpellBarStyle                          Data/UI/ + Resources/UI/SpellBarStyle.asset
PlayerController.TryCastFromHud        the only way the bar casts
HUDManager.SpellBar                    built beside the player panel, inside the HUD canvas
```

- **War shows what can be CAST, Peace what can be DONE.** War: every catalog spell action whose
  spell the character knows and whose key is live in War, keyboard order, groups of 5. Peace:
  Interactuar, Inventario, Mapa, Oficios, Misiones, Talentos, Grimorio. Both end in the posture
  switch, which shows where it LEADS (a leaf on War, blades on Peace) — the top-left chip already
  says where the player IS.
- **Face membership IS `InputContextPolicy.IsLive(action, stance)`**, the rule that decides whether
  the key works. A verb silenced for Peace in the Controls editor leaves the face, and nothing that
  reaches damage can be on the Peace face. `SpellBarModel` is pure so all of that is one fixture.
- **The bar never casts.** A click goes through `PlayerController.TryCastFromHud`, which applies
  every gate the key does (posture, per-slot mask, editor, `InputBlocker`, stun, spirit, dash,
  charge). The old bar called `SpellCaster.TryCast` directly — the Peace guarantee broken by a
  HUD button. `SpellBarHudTests` scans both sides: no cast call in `UI/HUD/SpellBar/`, all seven
  gates in `TryCastFromHud`, posture checked before the cast.
- **A verb goes to the SAME entry point its key or button uses** (`InventoryUI.SetVisible`,
  `MinimapHUD.ToggleWorldMap`, `PlayerInteractionController.TryInteractWith`,
  `CharacterSheetController.Open(TabSkills | TabGrimoire)`…). The bar is a second way in, never a
  second implementation. Verb states: available, idle (nothing in reach: dimmed, never hidden — a
  slot that vanishes as the player walks reads as flicker), active (its panel is open: a steady
  ring, because that is a state).
- **The flip is the bar's one loud event**, because it is the one moment every key under the
  player's fingers changes meaning: slots narrow to nothing left to right (in steps of two texels,
  so both edges stay on the grid), the face rebuilds behind them, they reopen; the gem is re-cut
  in the posture colour, the word rises, a curtain of motes lifts off the top edge. Everything
  else follows `HudMoteLayer`'s rule: events only, never at rest, nothing on a refusal.
- **The player panel's kit and grid.** Lives in `Valkur.UI` because `HudAbilitySlot` needs
  `SpellCaster`; drawn in the panel's texel space on the panel's bottom margin, centred, clamped
  clear of the panel. `SpellBarArt` is its own atlas on purpose — the shared kit is due to move
  (`.github/HUD_VISUAL_LANGUAGE.md`) and must not grow per surface. It re-cuts the gem on top of
  `HudArt.BakePanel` rather than copying the bake. **Padding is 6 texels**: at 4 the stone's corner
  rivets (4-5 texels in) peeked out from under the corner slots.
- **Retired with it:** the `SpellDragContext` / `DraggableSpellItem` / `DropZoneSpellSlot` drag
  chain (its only target was caster slots nothing casts for the player), the top-left
  `SpellCooldownHUD` text stack (every cooldown drawn a third time), and the Buildings editor's
  private spell-bar hiding (HUDManager hides the bar with the HUD for every editor).
- **Found on the way:** `CraftingPanelUI` was never instantiated in production — stations called
  `Instance?.OpenAt` on a null — so every trade was unreachable in play. `EntitySetup` builds it
  now and its art-less tray button is gone (Oficios is the way in). Slot 0 held
  `ProjectilePrefabFactory`'s nameless code-built fireball even with the catalog loaded; it takes
  the asset now. `InputCentralizationGuardTests` gained `Input.mousePosition`, which every pattern
  it had missed.
- **The big open gap: a spell with no key action cannot be on the War face**, because no key casts
  it — and the grimoire has 73 spells against 24 fixed spell actions. The fix is the loadout bar
  (`ActionBar/Slot1..N` actions whose payload is save data, filled by dragging from the grimoire),
  which is an input-model change across `InputActionCatalog`, `ValkurInputActions` and persisted
  `controls.json`, and was deliberately not folded into this pass.
- **Test trap:** a slot derives its state on its first `Tick`, so a fixture that asserts on
  `State` straight after `Create` reads `Empty` from a correct build.

## The music panel (bottom-right, above the tray)

Audited 2026-09-11 at **2.1/10** and rebuilt the same day to **9.1** — findings, per-axis
scores and the measured result in `.github/MUSIC_HUD_BEAUTY_AUDIT_2026-09-11.md`.

```text
MusicHudStyle          Data/UI/                 its own sizes/timings/resonance ramp + the tray icon
MusicPlayerHUD (+.Layout, .Window, .Playback, .Effects, .Hotkeys, .Console)  UI/HUD/Music/
MusicHudArt            UI/HUD/Music/            generated atlas: keys, glyphs, bead, medallion, sigils; plaque bake
MusicGroove / MusicVolumeNotches / MusicHudKey / MusicHudPointer                the widgets
MusicResonanceGraphic  UI/HUD/Music/            spectrum blocks + baked envelope + phrase marks, ONE graphic
MusicSpectrum          UI/HUD/Music/            pure FFT -> log bands -> automatic gain
MusicTrackInfo / MusicHudText / MusicSigil      zone, list position, time, Spanish key, labels
MusicSignalTap         Infrastructure/          signal ring buffer on each music source, volume divided back out
AudioManager.MusicWake Infrastructure/          wakes a music voice Unity started virtual
IMusicSignalSource     Core/                    AudioManager's read side of it (not on IAudioService)
music [abrir|cerrar|resonancia|reset]           DevConsole probe
```

- **The panel is a plaque, not a DAW.** The old one carried tap-tempo, a BPM drag and a
  waveform with a beat grid — authoring tools that wrote per-track tempo to PlayerPrefs and so
  calibrated ONE machine. Tempo is catalog data now; the panel only reads it.
- **All 24 tracks shipped with `bpm: 0` for the life of the project**, so the old metronome,
  bar counter, beat dots and grid never showed a real value. `tools/audio/analyze_music.py`
  (librosa, in `venv/`) now also bakes a 128-slice loudness `envelope`, and
  `patch_audio_catalog_bpm.py` writes `bpm`, `firstBeatOffsetSec`, `key`, `keyConfidence`,
  `envelope` and the full `beatTimes` into `Resources/AudioCatalog.asset`.
  `ShippedMusicCatalogTests` pins all of it.
- **`AudioSource.GetOutputData` / `GetSpectrumData` read AFTER the source volume**, so with the
  music slider at 0 a visualiser draws nothing (measured: peak 0.0000). `MusicSignalTap`
  captures the signal in `OnAudioFilterRead` — which ALSO sees it post-volume (measured at 0.02:
  filter peak 8.2e-3 vs output 9.1e-3) — and divides the source volume back out.
- **A music voice STARTED quieter than ~1e-3 is started virtual and its filter receives exact
  zeros — and it stays virtual.** Measured: 1e-5 and 3e-4 from a fade-in gave zeros, 1e-3 went
  real, and a voice once real STAYED real brought back to 1e-4 (filter peak 7e-5). So
  `AudioManager.MusicSilenceFloor` = 1e-4 (-80 dBFS: silent to the player, still a signal to
  divide) is necessary and not sufficient: when the tap reports `Starved` (0.3 s of zeros while
  playing below the wake level) `AudioManager.Update` lifts the voice to `MusicWakeVolume`
  (2e-3, -54 dBFS) for three frames and drops it back, at most every two seconds, never during
  a crossfade. Measured after: a muted player's resonance peaks at 0.257. The first floor (1e-5)
  and the second without the wake both passed a test and failed live — the experiment that
  "worked" had lowered a voice that was ALREADY real.
- **Every shipped track is `Streaming`**, which refuses `AudioClip.GetData`: the song's
  overview cannot be computed at runtime, only baked offline. That is what the `envelope` is.
- **The window grows; it never stretches.** The old panel resized by `localScale` per axis
  (measured 0.56 x 0.87, a 1.57x distortion of every glyph) on a canvas scaled against 800x600.
  The plaque is 126x42 texels on the HUD contract (1600x800, match 0.5, `HudLayout.MusicSortingOrder`
  140) and does not resize; `MusicPlayerHUDTests` refuses any non-unit `localScale` inside it.
- **126 texels is the tray's width** (3 x 80 + 2 x 6 = 252 px at scale 2), and the default dock
  (16, 104) puts the plaque's right edge on the tray's and 8 px above it: one column.
- **Nothing on the plaque moves with the beat.** A pulse is an event that repeats forever; one
  on the plaque is a screensaver. Motes answer a new track (notes from the medallion + a shine
  across the title + a gold rim), a skip, a seek, resuming and unmuting. The resonance — opened
  on request — may move with the music, and even there only one mote per BAR.
- **Gold is importance, the plaque is ambience:** no gold in the stone (`TheStone_HasNoGold`);
  gold only on the playhead bead and on the medallion for the moment a track starts.
- **A track that changed while the panel was closed is not announced when it opens**, and a
  keyboard skip with the panel closed emits nothing — both would fire stale.
- **`Gameplay/MusicPlayPause`, `MusicNext`, `MusicPrevious` ship with an EMPTY binding**, the
  editor toggles' shape: assignable in the Controls editor, never stealing a key. They work with
  the panel closed, behind the same gates as the world map key.
- **A frame step is capped at 1/20 s.** Uncapped, one long frame (an unfocused editor renders
  seconds apart) consumed a track change's whole gold flash, shine and notes before any of them
  was drawn — invisible to every EditMode test, which ticks in small steps.
- **A 3-texel groove cannot use a 2+2 nine-slice** (uGUI squashes it onto half texels — the XP
  bar's lesson again); it has its own 3x3 `WellThin`, and `EveryNineSlice_FitsItsBorders` walks
  every sliced image. Measured after the fix: every 2x2 block of the plaque uniform except the
  chamfered corners, where the world shows through.
- **The analysed key is shown only above 0.1 confidence** (13 of 24 tracks): below it the
  Krumhansl estimate is noise, and a panel that states noise as fact is less honest than one
  that says nothing. The `music` probe prints it with its confidence either way.
- **The GameObject keeps the name `MusicPlayerHUD`**: `BuildingsRuntimeEditor.HideHUDs` finds
  it by name.

## The inventory window

Audited 2026-09-11 at **2.1/10** and rebuilt the same day — findings, scores and what is open in
`.github/INVENTORY_HUD_BEAUTY_AUDIT_2026-09-11.md`; the shared rules it follows are
`.github/HUD_VISUAL_LANGUAGE.md` (sections 5.x are the window rules R13-R16).

```text
EquipmentLayout      Data/Items/            8 typed slots (EquipmentSlotKind) + the EquipSlot → slot map
Inventory.Equipment  Gameplay/Inventory/    CheckEquip / TryEquipFromBag / TryUnequip / RestoreEquipped
Inventory.Organize   Gameplay/Inventory/    SplitStack, SortBag (stable, merges stacks, refuses overflow)
HudTheme             Data/UI/               shared tokens + rarity ramp (Resources/UI/HudTheme.asset)
InventoryHudStyle    Data/UI/               the window's texels, timings, motes, sounds, TRAY ICON
InventoryUI (+.Build, .Window, .Refresh, .SlotInteraction, .Feel)   the window
InventoryArt / InventorySlotView / InventoryItemCard / InventoryConfirm / InventoryAudio /
InventoryPointerRelay                   Gameplay/Inventory/UI/
```

- **The equipment is TYPED, at the MODEL.** Nine untyped cells used to accept anything, and
  `EquipmentStatSource` summed every equippable item in all of them — nine longswords were +162
  melee damage; four labels (Arms, Gloves, Pants, Jewelry) named places no item could go. Now
  eight slots speak the data's `EquipSlot` vocabulary (aliases collapsed), and
  `TryDepositInEquipmentSlot` / `MoveSlotByIndex` refuse a wrong slot or an unmet level in EITHER
  direction of a swap, with `LastRefusal` saying why — so the world-drop path obeys it too.
  `levelRequirement` 1 means none (the model starts at level 0). Old saves (nine cells) restore
  through `RestoreEquipped`, which re-homes by kind and puts what fits nowhere in the bag.
- **The kit lives in `Valkur.UIKit` now** (`Gameplay/UIKit/Hud/`, namespace still
  `Valkur.UI.HUD`), because the inventory is `Valkur.Gameplay`, which may not reference
  `Valkur.UI` — that one rule is why every Gameplay window had grown its own dialect.
- **Same grid as the player panel**: `PlayerHudStyle.HudPixelScaleFor` whole texels, a `Pixels`
  child counter-scaled against the canvas, corner snapped to a whole screen pixel. Opens at
  `HudLayout.GameWindowRightInset` from the right — left of the minimap AND the music panel (R13);
  the old window hid the minimap completely. Position and collapse persist in PlayerPrefs
  (`valkur.inventory.*`), written at the END of a drag.
- **Pickups are found by DIFFERENCE.** Items reach the bag from a dozen places and only some raise
  events, so the window keeps a picture of every slot and compares on `OnInventoryChanged`; its own
  moves are bracketed by `BeginAction`/`EndAction` so a drag is never announced as a find. A find
  flashes its slot, throws motes in its rarity colour (a ring for Rare+, more for Legendary), marks
  it new until hovered, and counts on the tray button (`HUDIconBar.SetBadge`) while closed.
- **Rarity is SHAPE as well as colour**: corner marks 0/1/2/4/4 plus a one-texel filet for
  Legendary. The filet must NOT be a glow — a glow ring is what selection looks like, and the
  first capture read every legendary as the selected slot.
- **The card's numbers come from the code that applies them** (`EquipmentStatSource.AppendItem`
  and `StatModifier.Describe`), with a diff against what is worn in that slot. `Describe` itself was
  fixed on the way: it printed "-16.67% Attack Speed" for a cooldown REDUCTION (a buff under a
  minus sign) and "+0.1 Crit Chance" for 10 %; cooldowns now read as speed and fractions as %.
  `StatCatalog` names are Spanish now.
- **Stat and footer rows are FLOWED by measured ink** (`LayoutStats` / `LayoutFooter`): fixed
  quarters put "200" into the mana drop in the first live capture.
- **An Epic/Legendary drop to the ground asks first** (`InventoryConfirm`); nothing cheaper does —
  a window that confirms every pebble gets clicked through. Enter/Escape answer it before Escape can
  close the window.
- **Sounds**: catalogue id first (gated on `HasSfx`), else synthesised (`InventoryAudio`), played
  through `IAudioService.PlaySFX` so the effects volume applies. Only `inv_open` is recorded.
- **Data bug found by the first capture**: `iron_sword.asset` pointed at `ancient_relic_mask.png`;
  the equipped sword drew a mask. A catalogue sweep found it was the only one.
- **Test traps**: `InventoryUI.BuildFor(player)` / `TeardownForTests()` exist because Unity sends
  no Start or OnDestroy to a component added in Edit Mode, and `CurrencyWallet.OnCoinsChanged` is a
  STATIC event a fixture's window would otherwise stay subscribed to. A source guard that greps for
  `"Tab` matches `"Tab" + i`, and `| Q` matches `|| Quantity` — guard on the real phrases.

## The debug HUD (F1): an instrument in the tool dialect

Audited 2026-09-11 at **2.5/10** and rebuilt the same day to **8.3** — findings, measurements and
what is open in `.github/DEBUG_HUD_BEAUTY_AUDIT_2026-09-11.md`; the visual rules are section 6 of
`.github/HUD_VISUAL_LANGUAGE.md` ("same theme and grid as the HUD, different accent": no gold, no
game colours, a state triad of its own).

```text
Core/Diagnostics/          FrameTimeHistory, FrameHitchLog, ConsoleLogTally — pure, tested
Core/PerformanceMonitor    the ONE owner of frame measurement; samples every frame, open or not
Data/UI/DebugHudStyle      Resources/UI/DebugHudStyle.asset
UI/HUD/Debug/              DebugHUD (+.Build/.Readouts/.Events), DebugFrameGraph, DebugHudCounters,
                           DebugHudRow/Section/Art/Text/Nearby/Click
debughud [0-3|informe|copiar|reiniciar]   DevConsole, category "hud"
```

- **Levels: 0 hidden, 1 chip, 2 panel, 3 panel + `ai on` + combat ranges.** A release player
  stops at 1 (`RuntimeEditorPolicy`). Level 3 turns on only the overlays that were OFF and hands
  back exactly those. The level and the author's section folds persist in PlayerPrefs
  (`valkur.debughud.*`) — machine state, like the boot weights.
- **The old background measured 0 px tall for its whole life.** A `ContentSizeFitter` asks the
  `ILayoutElement`s on ITS OWN object; the only one was an `Image` with no sprite, whose preferred
  height is 0, and the TMP child was never consulted. It was legible only when the world behind it
  was dark — the hour of the day decided whether the tool worked. The panel lays itself out in
  texels now and no fitter is involved.
- **The band is derived:** `HudLayout.ToolColumnTop` (under the clock and stance chip) down to the
  combo badge on the player panel; `DebugHUD.BandTexels` is pure. It is ~181 texels at 1600x800 and
  less at 1080p, so sections FOLD like an accordion: the one the author opened last keeps its room,
  and a fold the screen made is never persisted as the author's choice.
- **Only the headers and COPIAR are raycast targets.** The combat poll refuses a click over any
  UI; a debug panel with a raycastable background is a dead zone for the left click wherever it sits.
- **Every row reads the system's own seam (H8)**: the mouse buttons from `PlayerController` with
  the BOOK cooldown (the old rows read `SpellCaster`'s internal slots, where 1-3 are empty for the
  player and printed "RDY"), CERCA from `EntityFaction.SideOf` sorted by distance (6 of the 11
  `EntityRegistry.Monsters` entries are neutral vendors — the old panel painted them as monsters).
- **`ZoneManager` is not in the ServiceLocator.** `ServiceLocator.Get<ZoneManager>()` answers null
  with one in the scene; the quest locator and the debug HUD fall back to a cached scene search.
- **The graph is a ring in a texture scrolled by UV**, one column per frame, against a FIXED 50 ms
  ceiling (an auto-scaling graph moves its budget lines whenever a spike arrives). A hitch writes a
  white-hot top texel INTO its column, so the mark travels with the frame and falls off with it.
- **Motes answer events only, and never land on a number.** The first build's GC puff was born on
  its own counter and turned "4.9" into an unreadable glyph; row events now rise from the frame's
  border and rings start outside the digits.
- **`ConsoleCommand.Handler` is an `Action<string[]>` that receives the command NAME in `args[0]`
  and DISCARDS the lambda's value.** `args => CmdBoot(args)` compiles and prints nothing: the `boot`
  probe shipped that way and `boot all` never worked. A command that returns its answer is
  registered as `args => Log(CmdX(args))` and reads its subcommand from `args[1]`;
  `DevConsoleHandlerContractTests` refuses the bare form.
- **A nearest-rank percentile needs an epsilon in float**: `0.99f * 100` is 99.0000x, so a bare
  `Ceiling` made the p99 of 1..100 come out 100.
- **Rewriting several files that depend on each other can break every session's compile.** Unity
  recompiled halfway through this rebuild (an interface had gained members its only implementer
  did not have yet) and blocked two other sessions' test runs. Make each save compile on its own,
  or write the dependents first.

## Incident reports

Past incidents that left investigation hooks behind. Read these first when a
related symptom reappears.

| Incident | When | Doc |
|---|---|---|
| Buildings save collapses `rel_x`/`rel_y` to one position per zone | 2026-05-08 (mitigated, root cause TBD) | `.github/incidents/BUILDINGS_SAVE_POSITION_COLLAPSE.md` |
| Run "twin-save" — duplicate `Saves/<runId>/` folders with byte-identical body but distinct `meta.run_id` | 2026-05-08 (mitigated — root cause: EditMode test pollution; fixed by `RefuseWriteOutsidePlayMode` guard) | `.github/incidents/RUN_TWIN_SAVE.md` |
| Spawners drift by their zone's origin on every restart (save wrote absolute world coords into a zone-relative field) | 2026-08-19 (fixed) | `.github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md` |
| 216 building templates (ids 4–313) deleted from the working tree; catalog rewritten without them | 2026-09-04 (recovered, root cause TBD) | `.github/incidents/BUILDING_TEMPLATES_MASS_DELETION.md` |
| Every placed particle wiped by an EditMode fixture — 188 emitters → 1, suite green | 2026-09-10 (fixed) | `.github/incidents/PARTICLE_INSTANCES_TEST_POLLUTION.md` |

## Open work

- **Editor UI/UX unification & persistence** — audited 2026-09-02, layer shipped 2026-09-03.
  The seventeen editors are 319 files / ~77.6k LOC and drifted: three (Camera, DungeonNodeGraph,
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
  `EditorWorkspaceContractTests` (16 tests). **Items is the first adopter** (2026-09-03):
  it implements `IProvidesWorkspaceState` and remembers mode, category tab, search text,
  hidden columns, the picked catalog item and the selected world drop — and its
  `TableColumnsConfig` PlayerPrefs entry is gone, the first of the three duplicates to go.
  **Tile followed the same day** and earned its place as the hard pilot: it exposed a
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
- **Day/night overhaul** — audited 2026-08-25 at **2.0/10**; Phases 0-3 shipped the same day, now **6.4/10**. The cycle used to reach no rendered pixel: three wrong URP enum literals (URP 14: `Freeform=1, Sprite=2, Point=3, Global=4`) left the scene light a `Point` of radius 1 and every placed torch a cookie-less `Sprite` light, while `WorldGridBuilder` forced the whole world to `Sprite-Unlit-Default` unconditionally. Now: typed URP API in all three light paths; world and entities lit (`Valkur/SpriteHDRTintLit`); placed lights on blend style **1 (Additive)**; colour from an 8-key Gradient in `Resources/DayNightProfile.asset`; the `Buildings/lights/` prop family emits its own light via `BuildingTemplateData.lightPresetKey` + `WorldLightLoader.RegisterDerivedLight` (derived lights are `persistent = false`, so `SaveAll` never writes them to `light_instances.json`); and a **`ScreenGradeFeature`** renderer feature on `Renderer2D.asset` does per-phase saturation/contrast/vignette/dither in one blit at a measured **0.215 ms/frame** — it does NOT need `renderPostProcessing`, so the ~18 ms UberPost stack stays off. Single owners: `GameplaySceneSetup.AmbientLitSortingLayerNames()` (light mask — DERIVED from TagManager minus the `AmbientUnlitSortingLayers` denylist, since 2026-09-08), `Core/Rendering/WorldSpriteMaterials` (lit vs unlit), `ScreenGradeSettings` (the live grade; static because Core cannot reference Gameplay). **URP 2D shadows render correctly but are disabled**: measured 11 % of pixels changed with a valid probe, yet URP derives the caster shape from the `Renderer` bounds, so every building throws a hard rectangular wedge. Accurate silhouettes would need the painted collision grid as caster geometry. NOTE `ShadowCaster2D.IsLit` reads `light.boundingSphere.radius`, written only by `Light2D.LateUpdate` — a light created and rendered in the same call has radius 0 and measures a false zero. Still open: atmosphere (3.0) and gameplay coupling (0.0), plus persisting the time of day and the Time & Weather editor's authoring. **The phases are pinned by 40 tests** across `DayNightPhaseLookTests` (reads the shipped `Resources/DayNightProfile.asset`, asserts characteristics not literals), `DayNightPipelineWiringTests` (the URP enum constants, exactly one Global light, the sorting-layer mask vs the layers that go lit, blend style 1 still Additive, the ScreenGrade feature still installed) and `TimeWeatherPhaseShortcutTests` (each Time & Weather phase button's hour, label and the phase the cycle actually reports there). Full findings and the roadmap: `.github/DAY_NIGHT_AUDIT_AND_ROADMAP.md`.
- **Weather (Wind / Rain / Snow)** — rebuilt 2026-08-30, zone-scoped 2026-09-01, in
  `Scripts/Gameplay/World/Weather/`. Weather is **stored per ZONE and rendered once**:
  `WeatherManager` holds a `zone -> levels` table and drives ONE set of effects at whatever
  the player's current zone asks for, so crossing a boundary retargets the existing effect
  and the fade turns that into a ramp. The Time & Weather editor authors the zone the player
  is standing in and
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
  anywhere, and it melts on a phase-dependent clock (Day 3.2x, Night 0.25x). Authored from **ESC → Time & Weather → Weather** (a row click
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
  from **ESC → Buildings → Door** or from the `door` / `doors` / `overlays` / `leave` console commands,
  both through the same `BuildingsRuntimeEditor.TrySetDoor` seams. Working example: building
  ID 64 (`houses/curse_house_topdown`) leads to
  `Maps/Interiors/house_interior_small.overlay.json`. Still open: interiors are bare rooms
  (no furniture, NPCs, loot or lighting), one file per doorway, no nesting, and no press-to-
  enter until the `E` double-binding is resolved. See `.github/BUILDING_DOORS_ROADMAP.md`.
- **Spell expansion — 27 new spells (46 → 73)** — designed AND implemented 2026-09-04; 71 grimoire nodes across the nine schools (was 44), plus the two innate spells. Full brief, per-spell data sheet, VFX specification and build order: `.github/SPELL_EXPANSION_27_ROADMAP.md`. The spells go into the nine EXISTING schools rather than new functional categories, because at the stated 100-spell target nine schools give ~11 nodes a tab while seven functional ones would give damage ~45; the cost of that is that FUNCTION becomes invisible, paid for by a `SpellNode.role` tag plus a grimoire filter. Three cost estimates were corrected by measurement while writing it, and each was a live defect in its own right (all three are FIXED now; they are kept here because the SHAPE of each is the thing worth recognising again): **`SummonExecutor` produces no creature** (a GameObject with a SpriteRenderer, a `CircleCollider2D`, `Health.Initialize(50)` and a despawn timer — no FSM brain, no `MeleeCombat`, no target acquisition, and a procedurally generated white circle for a sprite, so `summon_barbol` is a green blob that stands still); **`TotemController.HealTick()` heals only `_owner`**, with no radius sweep and no notion of an ally, so `healing_totem` is a stationary self-heal with a decorative pole; and **`SpellType.Trap` (8) and `SpellType.Shield` (9) are not in the dispatch table** and fall through to `ProjectileExecutor` with a warning, so neither value may be authored against. Eleven of the 27 are pure data today; the rest ride on eight systems, of which `BuffExecutor` (a `StatModifier[]` into the existing `TimedBuffSource`) has the highest leverage — it turns the whole support category into data for the run to 100 — and allied summon is the largest, because **no state class reads `stats.faction`** and the only thing that makes a faction peaceful today is the FSM set's allowed-state whitelist. `raise_thrall` is cast on a LIVING enemy and raises it if it dies while marked: the hook then lands while the entity still exists, which is strictly cheaper than a corpse registry and is also the better spell, since the player is betting before the kill. **What actually made summoning work was `FactionTargeting.EnemyOf`** — twenty FSM call sites moved off `EntityRegistry.Player`, so an ally hunts the nearest hostile and a monster can retarget to a nearer ally; the faction STRING remains inert. Deferred deliberately: `guardian_light`'s per-facet cracking, `animState` on all 27, icons, and a mastery-budget pass over the enlarged grimoire — all four are listed in Part 5 of the roadmap.
- **Economy** — audited 2026-09-07 at **3.9/10**, Phases 0-3 shipped and verified the same
  day, now **7.2**. Full findings, measured numbers, the Bitcoin decision and what remains:
  `.github/ECONOMY_AUDIT_AND_ROADMAP.md`; the design rules are in "The economy: a faucet, a
  spread, and a cycle" above. Shipped: the coin faucet (`MonsterDefinition.coinReward` +
  `DeathDropSystem.TryDropCoins` + `CoinDropSpawner`), `PlayerDefinition.startingCoins`, the
  65 unpriced items given a rarity ladder, `EconomyGroupDefinition.typeMargins` with five
  seeded groups, live persona negotiation (and the sell-side direction fix that came with
  it), the deterministic `MarketCycle` / `MarketService` with save persistence and a console,
  and vendor purses with proportional restocking (`VendorNPC.Purse.cs`, `coinFloat` 400 /
  `restockSeconds` 600). Verified: full EditMode suite 7841 tests with no economy failure,
  the five economy fixtures 33/33, and shipped data re-read from disk beside memory with 0
  disagreements. **Still open, roughly in order of leverage:** real sinks
  (repair, fast travel, respec cost, storage, housing) — the cycle currently moves prices in
  a world whose only purchase is inventory; coupling gold to POWER, since talents and spells
  cost points and gold buys nothing the player wants; a gathering route for the cooking
  ingredients (`beef`, `potato`, `onion`, `herbs` are produced by no harvest table, so
  cooking is a sink with no input); a coin field on `QuestDefinition` (which has none, and
  zero quests ship); and economic telemetry — coins minted and burned per session, so
  inflation is a number rather than a feeling.

- **Spawners** — audited 2026-09-07 at **4.1/10**, steps 1-8 shipped the same day, now
  **7.6**. Findings, measurements and what is left in
  `.github/SPAWNER_AUDIT_AND_ROADMAP.md`; the design rules are in "Spawners: a preset is a
  starting point" below. What is still open: the catalogue cleanup (25 presets collapse to
  ~6 once the placements are self-sufficient — deliberately held until the migrated file has
  been read by a human), a collider check at placement time, and saving a placement as a new
  preset (`UpsertTemplate` still has no runtime caller).
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
