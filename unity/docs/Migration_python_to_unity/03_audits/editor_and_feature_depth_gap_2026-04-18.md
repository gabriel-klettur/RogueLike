# Editor and Feature Depth Gap Analysis — 2026‑04‑18

**Author:** Migration audit (Copilot agent, deep-dive pass)
**Status:** ⚠️ Critical correction to the published roadmap
**Scope:** Compare every Python module under `python/src/` against its Unity counterpart under `unity/Valkur/Assets/_Project/Scripts/` at the **detail level** (panels, sub-panels, buttons, modes, undo/redo, services), not just at the “system exists / system missing” level.

---

## 1. Executive summary

The published roadmap (see [roadmap_50_steps.md](../01_execution/roadmap_50_steps.md)) reports **45 / 50 ≈ 90 % completed** and the parity matrix in [phase_00_baseline_and_parity.md](../01_execution/phase_00_baseline_and_parity.md) marks all P0–P3 systems as *DONE*. That is true at **system shell level** — every major Python subsystem has a runtime equivalent in Unity, and 239 EditMode tests pass with 0 errors / 0 warnings.

However a file-by-file inventory shows that **the in-game editors and several engine helpers were ported as thin shells**. The user-facing functionality (panels, sub-panels, toolbars, asset grids, undo/redo, persistence helpers, tutorial overlays) is **5 % – 15 % migrated**, not 100 %.

| Domain | Python LOC | Unity LOC | Coverage |
|---|---:|---:|---:|
| 11 in-game editors (combined) | ~63 000 | ~3 400 | **≈ 5 %** |
| UI widget framework (`roguelike_ui/widgets/`) | ~4 200 | ~600 | ≈ 14 % |
| Engine helpers (`roguelike_engine/`) – diagnostics, console, chat router, profile, init | ~9 500 | ~1 800 | ≈ 19 % |
| ECS gameplay systems / managers | ~45 700 | ~30 000 | **≈ 65 %** ✅ |
| Data importers (one-shot, directional Py→Unity) | n/a | full | ✅ |

Net effect: **core gameplay parity is real**, but everything that lets a designer *author* content inside the running game is largely missing. Designers can play Valkur in Unity; they cannot yet *edit* it the way they can in Python.

---

## 2. Method

For each of the 11 editor modules and 8 gameplay/engine modules:

1. Counted files and lines in `python/src/...` (PowerShell `Get-ChildItem … Measure-Object`).
2. Located the Unity counterpart (runtime editor in `Scripts/Gameplay/Editors/`, EditorWindow in `Scripts/Editor/`, or system folder in `Scripts/Gameplay/`).
3. Read the Python `README_*.md` (where present) and at least one `editor_view.py` / `*_controller.py` to enumerate panels, sub-panels, toolbars, buttons, modes, services, undo targets.
4. Read the equivalent Unity `.cs` to enumerate what is actually wired.
5. Diffed the two.

Evidence files referenced are linked inline.

---

## 3. Editors gap matrix (level: panel / button / service)

Legend: 🟢 = ported, 🟡 = partial / stubbed, 🔴 = missing.

### 3.1 Entities Editor (Python F5 → Unity F5)

| # | Python feature | Source | Unity status |
|---|---|---|---|
| 1 | Title panel (`title/`) | [title_controller.py](../../../../python/src/roguelike_editors/entities/title/title_controller.py) | 🟡 plain title bar only |
| 2 | Toolbar panel (`toolbar/`) with mode icons | [toolbar/](../../../../python/src/roguelike_editors/entities/toolbar/) | 🟡 3 buttons (Select/Spawn/Delete) — no icons, no shortcuts, no tooltip |
| 3 | Add/Remove panel (modal, asset picker, confirmation) | [add_remove/](../../../../python/src/roguelike_editors/entities/add_remove/) | 🔴 missing (Spawn/Delete are direct, no modal) |
| 4 | Picker panel (categorized grid: Players / Hostiles / Neutrals) | [picker/](../../../../python/src/roguelike_editors/entities/picker/) | 🟡 only Players + Hostiles tabs — no Neutrals, no search, no sort |
| 5 | Properties panel (right) — text + per-type forms | [properties/](../../../../python/src/roguelike_editors/entities/properties/) | 🟡 read-only `TMP` text, no editing |
| 6 | Tutorial panel (toggleable help overlay) | [tutorial/](../../../../python/src/roguelike_editors/entities/tutorial/) | 🔴 missing |
| 7 | Sub-module: `info` (entity metadata) | [info/](../../../../python/src/roguelike_editors/entities/info/) | 🔴 missing |
| 8 | Sub-module: `state_tabs` (per-FSM-state tabs) | [state_tabs/](../../../../python/src/roguelike_editors/entities/state_tabs/) | 🔴 missing |
| 9 | Sub-module: `type_assets` + `assets_subtabs` + `assets_grid` (sprite grid w/ thumbnails) | [type_assets/](../../../../python/src/roguelike_editors/entities/type_assets/) | 🔴 missing (no thumbnail grid) |
| 10 | Service: undo / redo (command stack with history) | [services/history/](../../../../python/src/roguelike_editors/entities/services/) | 🔴 missing |
| 11 | Service: ECS snapshot for safe rollback | services/ecs_snapshot | 🔴 missing |
| 12 | Service: spawn pipeline (validates before placement) | services/spawn | 🟡 direct create_entity; no validation |
| 13 | Service: stats templates per type | services/stats_templates | 🔴 missing |
| 14 | Service: rename + persistence with intentional null | services/rename | 🔴 missing |
| 15 | Service: history persistence on close | services/history | 🔴 missing |

**Coverage: ≈ 8 %** of feature surface. Counterpart code: [EntitiesRuntimeEditor.cs](../../../Valkur/Assets/_Project/Scripts/Gameplay/Editors/EntitiesRuntimeEditor.cs) (381 LOC vs Python 6 767 LOC across 106 files).

> Reference contract: `python/src/roguelike_editors/entities/README_ENTITIES.md` (140 lines) lists the MVC architecture, command stack, sub-panels, asset grid, rename, history. **None of those advanced affordances exist in Unity yet.**

### 3.2 Spells Editor (Python F4 → Unity F4)

| # | Python feature | Unity status |
|---|---|---|
| 1 | Spell picker grid with icons | 🟢 [SpellsRuntimeEditor.cs](../../../Valkur/Assets/_Project/Scripts/Gameplay/Editors/SpellsRuntimeEditor.cs) |
| 2 | Properties pane with all spell parameters | 🟡 read-only label dump |
| 3 | Per-spell sub-tabs (timings / damage / particles / VFX) | 🔴 missing |
| 4 | Live test cast button | 🔴 missing |
| 5 | Save/load preset (JSON round-trip) | 🟢 via [SpellsEditorWindow.cs](../../../Valkur/Assets/_Project/Scripts/Editor/SpellsEditorWindow.cs) (Save All) |
| 6 | Tutorial / help overlay | 🔴 missing |
| 7 | Add/Remove spell entry (with modal) | 🟡 “New Spell” button in EditorWindow only — no in-game flow |
| 8 | Undo/redo | 🔴 missing |
| 9 | Particle preset linker (drag-and-drop from Particles editor) | 🔴 missing |
| 10 | Cooldown visualizer | 🔴 missing |

**Coverage: ≈ 25 %** (best of the editors thanks to the EditorWindow companion).

### 3.3 FSM Editor (Python F12 → Unity F8)

| # | Python feature | Unity status |
|---|---|---|
| 1 | Graph panel with nodes / edges | 🟡 [FSMRuntimeEditor.cs](../../../Valkur/Assets/_Project/Scripts/Gameplay/Editors/FSMRuntimeEditor.cs) shows current state list |
| 2 | Toolbar: Select / Add Node / Clone / Connect / Disconnect / Delete / Mark Init / Mark End / Zoom +/- (10 tools, with hover/blink, see [toolbar_graph_panel_view.py](../../../../python/src/roguelike_editors/fsm/fsm_graph_panel/toolbar_graph_panel/toolbar_graph_panel_view.py)) | 🔴 none of these tools exist |
| 3 | Pan / zoom canvas | 🔴 missing |
| 4 | Per-node properties panel | 🔴 missing |
| 5 | Transition condition editor | 🔴 missing |
| 6 | FSM set save / load (json round-trip) | 🟡 read-only display |
| 7 | Animation map binding UI | 🔴 missing |
| 8 | Undo / redo | 🔴 missing |
| 9 | Per-state tab (Attack / Idle / Damage / Death …) | 🔴 missing |
| 10 | Live state highlight while game runs | 🟡 displays current state name only |
| 11 | Tutorial overlay | 🔴 missing |

**Coverage: ≈ 6 %**. Python: 159 files / 10 158 LOC. Unity: 655 LOC.

### 3.4 Buildings Editor (Python F2 → Unity F2)

| # | Python feature | Unity status |
|---|---|---|
| 1 | Building catalog grid | 🟢 [BuildingsRuntimeEditor.cs](../../../Valkur/Assets/_Project/Scripts/Gameplay/Editors/BuildingsRuntimeEditor.cs) |
| 2 | Place / remove on tile grid (with snap) | 🟢 |
| 3 | Toolbar (mode buttons + tutorial) — see [events/toolbar_panels.py](../../../../python/src/roguelike_editors/buildings/events/toolbar_panels.py) | 🟡 only mode buttons, no tutorial panel |
| 4 | Add/Remove modal (search, filter, picker) | 🔴 missing |
| 5 | Properties pane (per-instance: split_ratio, scale, z_top/z_bottom) | 🟡 read-only label, no edit |
| 6 | Per-zone filter | 🔴 missing |
| 7 | Bulk operations (multi-select + delete/duplicate) | 🔴 missing |
| 8 | Undo / redo | 🔴 missing |
| 9 | Asset thumbnail with preview | 🟡 sprite icon only, no metadata tooltip |
| 10 | Companion EditorWindow | 🟢 [BuildingsEditorWindow.cs](../../../Valkur/Assets/_Project/Scripts/Editor/BuildingsEditorWindow.cs) |

**Coverage: ≈ 30 %**. Python 120 files / 6 902 LOC. Unity ≈ 900 LOC.

### 3.5 Spawner Editor (Python F3 → Unity F3)

| # | Python feature | Unity status |
|---|---|---|
| 1 | Place spawner on map | 🟢 [SpawnerEditorManager.cs](../../../Valkur/Assets/_Project/Scripts/Gameplay/Spawners/SpawnerEditorManager.cs) |
| 2 | Per-template wave editor (count, delay, prototype, spread) | 🔴 missing |
| 3 | Trigger configurator (proximity radius, auto_start, mixed_mode) — see [spawner_trigger_system.py](../../../../python/src/roguelike_game/ecs/systems/spawner/spawner_trigger_system.py) | 🔴 missing |
| 4 | Policy editor (loop, advance_on, max_active, count_ko_as_clear) | 🔴 missing |
| 5 | Defend-spawn radius + leash editor | 🔴 missing |
| 6 | Per-state visual mapping (`state_visuals`) | 🔴 missing |
| 7 | Per-state HP pool (`hp_scope`, `life_defaults`, `visuals_life`) — see [spawner_damage_system.py](../../../../python/src/roguelike_game/ecs/systems/spawner/spawner_damage_system.py) | 🔴 missing |
| 8 | FSM-set picker per spawner | 🔴 missing |
| 9 | Auto-repair visuals + preflight validator (see [visuals_auto_repair.py](../../../../python/src/roguelike_game/ecs/systems/spawner/placement/visuals_auto_repair.py)) | 🔴 missing |
| 10 | Persistence to `spawners_instances.json` | 🔴 missing |
| 11 | Wave debug overlay | 🟡 SpawnerDebugRenderSystem ported, but no editor UI to enable/disable per spawner |
| 12 | Undo / redo | 🔴 missing |

**Coverage: ≈ 10 %**. Python 167 files / 13 699 LOC (the largest editor). Unity 4 files / ~600 LOC.

### 3.6 Tile Editor (Python → Unity F6 / TileEditor*)

| # | Python feature | Unity status |
|---|---|---|
| 1 | Tile palette with categories | 🟢 [TileEditor*](../../../Valkur/Assets/_Project/Scripts/Gameplay/TileEditor/) |
| 2 | Brush sizes (1×1, 3×3, 5×5) | 🟡 single size |
| 3 | Layer toggles (ground / decals / walls) | 🟡 partial |
| 4 | Eyedropper tool | 🔴 missing |
| 5 | Fill bucket | 🔴 missing |
| 6 | Rectangle / line tool | 🔴 missing |
| 7 | Per-zone filter | 🔴 missing |
| 8 | Auto-tile rules editor | 🔴 missing |
| 9 | Undo / redo | 🔴 missing |
| 10 | Save tilemap to JSON | 🟢 via importer |
| 11 | Tutorial panel | 🔴 missing |

**Coverage: ≈ 35 %**. Python 51 files / 4 657 LOC. Unity 15 files / ~2 500 LOC. Strongest of the editors.

### 3.7 Map Editor (Python → Unity MapEditor*)

| # | Python feature | Unity status |
|---|---|---|
| 1 | Multi-zone visualization | 🟢 [MapEditor*](../../../Valkur/Assets/_Project/Scripts/Gameplay/MapEditor/) |
| 2 | Zone offsets editor | 🟡 read-only |
| 3 | Portal placement / linker | 🔴 missing |
| 4 | Region naming | 🔴 missing |
| 5 | Minimap preview within editor | 🔴 missing |
| 6 | Save / load world JSON | 🟢 via importer |
| 7 | Bulk zone operations | 🔴 missing |

**Coverage: ≈ 30 %**.

### 3.8 Items Editor (Python → Unity F7)

| Feature | Unity status |
|---|---|
| Item picker grid | 🟢 [ItemsRuntimeEditor.cs](../../../Valkur/Assets/_Project/Scripts/Gameplay/Editors/ItemsRuntimeEditor.cs) |
| Edit name / icon / stackable / type | 🟡 read-only |
| Add new item via in-game flow | 🔴 missing |
| Per-type sub-tabs (food / weapon / shield / potion / consumable) | 🔴 missing |
| Drag-drop into vendor inventories | 🔴 missing |
| Undo / redo | 🔴 missing |

**Coverage: ≈ 20 %**.

### 3.9 Inventory Editor (Python F11 → Unity F11)

| Feature | Unity status |
|---|---|
| Side selector (default / active player) — see [editor_view.py](../../../../python/src/roguelike_editors/inventory/editor_view.py) | 🔴 missing |
| Category tabs (player / monsters / hostile) | 🔴 missing |
| Inventory grid editor with drag-and-drop slots | 🟡 read-only display in [InventoryRuntimeEditor.cs](../../../Valkur/Assets/_Project/Scripts/Gameplay/Editors/InventoryRuntimeEditor.cs) |
| Item selection panel (right pane) | 🔴 missing |
| Save default vs save active button | 🔴 missing |
| Show default vs show active toggle | 🔴 missing |
| Live ECS sync of player inventory | 🔴 missing |
| Add item flow (wizard) | 🔴 missing |
| Undo / redo | 🔴 missing |

**Coverage: ≈ 10 %**.

### 3.10 Particles Editor (Python F9 → Unity F9)

| Feature | Unity status |
|---|---|
| Preset list | 🟢 [ParticlesRuntimeEditor.cs](../../../Valkur/Assets/_Project/Scripts/Gameplay/Editors/ParticlesRuntimeEditor.cs) + [ParticlesEditorWindow.cs](../../../Valkur/Assets/_Project/Scripts/Editor/ParticlesEditorWindow.cs) |
| Live preview viewport | 🟡 fires preset on player |
| Per-emitter parameter editor | 🔴 missing |
| Color gradient editor | 🔴 missing |
| Curve editor (size / alpha over lifetime) | 🔴 missing |
| Save / load preset | 🟢 via EditorWindow |
| Drag preset to spell / building | 🔴 missing |
| Tutorial | 🔴 missing |

**Coverage: ≈ 25 %**.

### 3.11 Lighting Editor (Python F10 → Unity F10)

| Feature | Unity status |
|---|---|
| Day/night cycle viewer | 🟢 [LightingRuntimeEditor.cs](../../../Valkur/Assets/_Project/Scripts/Gameplay/Editors/LightingRuntimeEditor.cs) (uses reflection on Light2D) |
| Per-torch color/intensity editor | 🟡 partial |
| Ambient gradient editor | 🔴 missing |
| Save lighting profile JSON | 🔴 missing |
| Light placement tool | 🔴 missing |
| Tutorial | 🔴 missing |

**Coverage: ≈ 20 %**.

---

## 4. Runtime / gameplay gaps (non-editor)

Even though the parity matrix marks these as DONE, the user-visible feature set is thinner than Python. These are **smaller**, **discrete** gaps — usually one button, one option, or one behavior.

### 4.1 Vendors / economy
- Python supports `economy_group` profiles with **whitelist / blacklist / margins** loaded from `data/vendors/economy/groups/*.json` (see [economy_service.py](../../../../python/src/roguelike_game/ecs/systems/vendors/services/economy_service.py)). Unity vendor logic does not yet apply group margins.
- Python supports **persona-based negotiation** (`PersonaService`) — unported.
- Python has SQL fallback for prices via `item_prices` table (see [price_service.py](../../../../python/src/roguelike_game/ecs/systems/vendors/services/price_service.py)) — Unity uses static JSON.
- Vendor UI in Python: scrollable panel with **adaptive width**, **paginated rows**, **per-row +1 / −1 buttons**, **scrollbar with thumb clamping** (see [vendor_ui_system.py](../../../../python/src/roguelike_game/ecs/systems/vendors/vendor_ui_system.py) — 260+ LOC of UI math). Unity equivalent: simpler grid, no pagination, no scroll math.

### 4.2 FSM gameplay
- Python FSM supports JSON-defined transitions (`when: after_attack`, `from`, `to`) — see [fsm_system.py](../../../../python/src/roguelike_game/ecs/systems/fsm/fsm_system.py).
- Per-state allow-list (`allowed_state_classes`, `allow_death`, `allow_damage`, `allow_unconscious`) — see [fsm.py](../../../../python/src/roguelike_game/ecs/systems/fsm/fsm.py).
- `_EntityProxy` indirection so states can mutate `world.components` safely.
- Frustum-culled FSM updates with per-state criticality (`OFFSCREEN_UPDATE_INTERVAL = 8`).
- Stun probability per damage event (`stop_probability = 0.25` default) — verify ported.

### 4.3 Spawner runtime
- Per-spawner FSM set (`fsm_set_id`, `fsm_set_params`) with override per instance.
- Health pools per state OR shared (`hp_scope`).
- **Auto-repair visuals**: `auto_repair_state_visuals` recreates missing `Building` instances on startup (see [visuals_auto_repair.py](../../../../python/src/roguelike_game/ecs/systems/spawner/placement/visuals_auto_repair.py)).
- **Preflight validator** scans all instances on game launch and writes back to JSON.
- Hardcoded narrative hooks: `_spawn_felipondor_and_portal` triggers Felipondor + lobby portal when `survival_10` completes.
- “Mixed mode” trigger (proximity once + cooldowns thereafter).

### 4.4 Input
- Python supports **multi-binding per action** (multiple keys → OR), and runtime reload of bindings every frame (see [input_system.py](../../../../python/src/roguelike_game/ecs/systems/input/input_system.py)).
- F4: live reload of `spells.json` while playing.
- Per-spell rising-edge detection per binding source (kb_a, kb_b, mouse).
- Suppression flags computed each frame from inventory drag, modal open, etc.
- Unity: New Input System asset wired but does not yet expose multi-binding configurator UI in pause menu.

### 4.5 Chat / dialogue
- Python `ChatRouterSystem` routes commands to vendor / dialogue / quest sub-handlers.
- Floating chat bubble per NPC with TTL, color, icon.
- Vendor sub-router triggers `VendorUISystem`.
- Unity `ChatSystem` covers basic open/close + bubble; sub-router commands are partial.

### 4.6 Diagnostics / dev tools
- Python `roguelike_engine/diagnostics/` (1 988 LOC): per-frame profiler, system timing graph, memory graph, log replay.
- Python `roguelike_engine/console/` (1 266 LOC): in-game dev console with command history.
- Unity equivalents: only `DebugHUD.cs` (~120 LOC). **Net gap: ~3 100 LOC of dev tools.**

### 4.7 Menus / configurators
- Python `roguelike_ui/widgets/menu_configurator/`, `sounds_configurator/`, `options_configurator/`, `params_editor_ui/`, `file_system_picker/` (~2 000 LOC). These power the in-game options menus.
- Unity `Scripts/UI/PauseMenu/` exists with `InputsPanel`, `SoundsPanel`, `LoadPanel`, `Actions`, `Builder` — but each is a thin shell. No per-binding rebind UI, no audio bus mixer, no save-slot manager, no file picker dialog.

### 4.8 HUD
- Python HUD (`roguelike_ui/hud/`): action grid (8 slots with cooldowns), input profile picker, orchestrator that re-layouts on resize.
- Unity HUD (`Scripts/UI/HUD/`): PlayerHUD, ComboHUD, DashMeterHUD, SpellBarHUD, NamePlate, TargetHUD, MinimapManager, DebugHUD — most exist 🟢 but:
  - **PlayerHUD.Mana** not connected (Apr 2026 audit, finding H4).
  - **FloatingDamageNumber** lacks pooling (finding H5).
  - SpellBarHUD does not display per-slot cooldown ring.
  - Action grid input rebind not exposed.

### 4.9 Save / persistence
- Python uses SQLAlchemy + Alembic migrations (see `python/alembic/`) for **versioned save files** with `Item`, `ItemPrice`, vendor registry tables.
- Unity uses JSON only (`Scripts/Gameplay/Save/`). Save versioning is single-version. **No migration path** if save format changes.

### 4.10 Minimap / world
- Python minimap (430 LOC): per-zone tinting, fog-of-war, NPC dots, vendor markers.
- Unity `MinimapManager.cs`: world-space camera-on-stick — no fog-of-war, no markers besides player.

---

## 5. Aggregated coverage table

| Module | Python files | Python LOC | Unity files (rt + edit) | Unity LOC | Coverage |
|---|---:|---:|---:|---:|---:|
| Editor: spawner | 167 | 13 699 | 4 | ~600 | **5 %** |
| Editor: fsm | 159 | 10 158 | 1 | 655 | **6 %** |
| Editor: buildings | 120 | 6 902 | 2 | ~900 | **13 %** |
| Editor: entities | 106 | 6 767 | 1 | 381 | **6 %** |
| Editor: spells | 53 | 5 346 | 2 | ~600 | **11 %** |
| Editor: tiles | 51 | 4 657 | 15 | ~2 500 | **54 %** |
| Editor: items | 56 | 4 243 | 1 | 283 | **7 %** |
| Editor: inventory | 87 | 3 602 | 1 | 228 | **6 %** |
| Editor: map | 65 | 3 596 | 9 | ~1 800 | **50 %** |
| Editor: particles | 44 | 2 914 | 2 | ~700 | **24 %** |
| Editor: lighting | 23 | 2 037 | 1 | 504 | **25 %** |
| **Editors total** | **935** | **63 921** | **39** | **~9 151** | **≈ 14 %** |
| UI widgets | 44 | 4 183 | ~12 | ~600 | 14 % |
| Engine helpers (diagnostics + console + chat router + init) | ~120 | ~9 500 | ~10 | ~1 800 | 19 % |
| ECS systems / managers | ~470 | ~45 700 | ~120 | ~30 000 | **65 %** |

(Unity LOC includes both runtime editor `.cs` and EditorWindow `.cs`.)

---

## 6. Recommended remediation plan

### Phase 8 (proposed) — Editor depth migration

| Step | Title | Priority | Estimate |
|---|---|---|---|
| 51 | Establish runtime UI widget kit (`UIToolkitOrIMGUI.md`) — undo stack, modal, asset grid, scrollable list, tabs | High | M |
| 52 | Buildings editor full parity (toolbar + add/remove modal + tutorial + properties edit + undo) | High | M |
| 53 | Entities editor full parity (state tabs, asset thumbnail grid, info panel, rename, undo) | High | L |
| 54 | Spawner editor: wave / trigger / policy / state-visuals / hp-pools / fsm-set forms | High | XL |
| 55 | FSM editor: graph canvas, 10-tool toolbar, transition condition editor, json round-trip | High | XL |
| 56 | Spells editor: per-tab (timings/damage/particles/VFX), live test cast, particle preset linker | Med | M |
| 57 | Inventory editor: side / category / drag-drop slots / save default/active | Med | M |
| 58 | Items editor: per-type sub-tabs, add-item wizard | Med | S |
| 59 | Tiles editor: brush sizes, eyedropper, fill, rectangle, autotile | Med | M |
| 60 | Map editor: portals, region naming, in-editor minimap | Med | M |
| 61 | Particles editor: per-emitter params, gradient editor, curve editor | Low | M |
| 62 | Lighting editor: per-torch params, ambient gradient, save profile | Low | S |
| 63 | UI widget framework completion (file picker, options configurator, params editor, sounds configurator) | Med | L |
| 64 | Dev console + diagnostics overlay parity | Low | M |
| 65 | Save/load versioning + migration path (parity with Alembic) | Med | M |

Each step should ship with: per-feature checklist (use Python README as contract), undo/redo plumbing, EditMode tests covering at least open/close + one CRUD round-trip, and PlayMode smoke test.

### Phase 9 (proposed) — Runtime feature polish
- Vendor UI scrollbar + pagination + per-row buttons.
- HUD action grid rebind + cooldown rings.
- Minimap fog-of-war + markers.
- Multi-binding configurator in Pause / Inputs.
- Chat router sub-handlers parity.
- Save versioning.

---

## 7. What is NOT a gap (clarifications)

These were sometimes called out as gaps but **are intentional or already done**:

- ✅ ECS → MonoBehaviour conversion: complete and idiomatic. Not all 469 Python ECS files map 1:1 — Unity collapses many small systems into larger MonoBehaviours by design.
- ✅ Pyglet / pygame rendering pipeline: replaced by URP 2D. No port needed.
- ✅ Pydantic schemas: replaced by ScriptableObject definitions in `Scripts/Data/`.
- ✅ SQLAlchemy/SQLite for runtime: Unity uses ScriptableObjects + JSON. SQL only existed because Python re-generated catalogs from designer-edited SQL — Unity uses the importer (`Scripts/Editor/PythonDataMigrator*.cs`) once.
- ✅ Pylos / Soluna minigames: explicitly out of scope per `copilot-instructions.md`.
- ✅ Asset migration (Pasos 20–22): pending but tracked separately in [phase_02_asset_pipeline_plan.md](../02_assets/phase_02_asset_pipeline_plan.md).

---

## 8. References

- Roadmap (current): [roadmap_50_steps.md](../01_execution/roadmap_50_steps.md)
- Parity matrix (current): [phase_00_baseline_and_parity.md](../01_execution/phase_00_baseline_and_parity.md)
- Last architectural audit: [architectural_audit_2026-04-08.md](architectural_audit_2026-04-08.md)
- Python entities editor contract: `python/src/roguelike_editors/entities/README_ENTITIES.md`
- Python spells editor contract: `python/src/roguelike_editors/spells/README_SPELLS.md`
- Python tiles editor contract: `python/src/roguelike_editors/tiles/README_TILES.md`
- Per-editor checklists (new): [01_execution/editors/](../01_execution/editors/)

---

## 9. Bottom line

> The migration is **gameplay-complete** but **authoring-incomplete**.
> A player can play Valkur in Unity. A designer cannot yet *make* Valkur in Unity.
>
> Closing the gap is **Phase 8 + Phase 9**, an estimated 15 additional roadmap steps (51–65) totaling roughly **40 000 net LOC of UI / editor work**, before the Unity port reaches feature parity with the Python source.
