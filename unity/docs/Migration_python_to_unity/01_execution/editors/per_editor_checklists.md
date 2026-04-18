# Per-Editor Migration Checklists — 2026‑04‑18

These checklists translate the gap analysis ([editor_and_feature_depth_gap_2026‑04‑18.md](../../03_audits/editor_and_feature_depth_gap_2026-04-18.md)) into actionable per-editor work items.

Each item lists:

- **Source contract** (Python file / README)
- **Current Unity state** (existing file)
- **Acceptance criterion**

Status keys: `[ ]` not started · `[~]` in progress · `[x]` done.

---

## 1. Buildings Editor (F2)

Source: `python/src/roguelike_editors/buildings/` (120 files / 6 902 LOC)
Target: `Scripts/Gameplay/Editors/BuildingsRuntimeEditor.cs` + `Scripts/Editor/BuildingsEditorWindow.cs`

- [ ] Toolbar with icons (Place / Remove / Pick / Tutorial) — see `events/toolbar_panels.py`
- [ ] Add/Remove modal (catalog search + filter + confirmation)
- [ ] Properties panel: edit `split_ratio`, `scale`, `z_top`, `z_bottom` per instance
- [ ] Per-zone filter dropdown
- [ ] Bulk select + delete + duplicate (Shift / Ctrl click)
- [ ] Asset thumbnail tooltip with metadata
- [ ] Tutorial overlay (toggle key)
- [ ] Undo / redo stack (command pattern, capped at 50 entries)
- [ ] Persist to `buildings_instances.json` round-trip
- [ ] EditMode test: open → place → undo → assert state restored
- [ ] PlayMode smoke: F2 toggles editor without exception

---

## 2. Entities Editor (F5)

Source: `python/src/roguelike_editors/entities/` (106 files / 6 767 LOC, see `README_ENTITIES.md`)
Target: `Scripts/Gameplay/Editors/EntitiesRuntimeEditor.cs` (381 LOC)

- [ ] Title panel with category icon
- [ ] Toolbar with icons (Select / Spawn / Delete / Pick / Rename)
- [ ] Add/Remove modal with monster picker
- [ ] Picker grid with **Players / Hostiles / Neutrals** tabs (Neutrals missing)
- [ ] Search box + sort by name/level
- [ ] Properties pane editable (not read-only) — name, hp, level, faction
- [ ] Sub-module: `info` (entity metadata block)
- [ ] Sub-module: `state_tabs` per FSM state
- [ ] Sub-module: `type_assets` + `assets_subtabs` + `assets_grid` (sprite thumbnail grid)
- [ ] Tutorial overlay
- [ ] Service: undo/redo with command stack
- [ ] Service: ECS snapshot rollback
- [ ] Service: spawn validation (anchor reachable, no overlap)
- [ ] Service: stats templates per type (player / hostile / neutral)
- [ ] Service: rename + persist with intentional null handling
- [ ] Service: history persistence (save on close, restore on open)
- [ ] EditMode tests for each service
- [ ] PlayMode test: spawn → kill → respawn cycle

---

## 3. Spells Editor (F4)

Source: `python/src/roguelike_editors/spells/` (53 files / 5 346 LOC, see `README_SPELLS.md`)
Target: `Scripts/Gameplay/Editors/SpellsRuntimeEditor.cs` + `Scripts/Editor/SpellsEditorWindow.cs`

- [ ] Per-spell sub-tabs: Timings / Damage / Particles / VFX / Audio
- [ ] Live test cast button (fires spell on player at cursor)
- [ ] Particle preset linker (drag from Particles editor)
- [ ] Cooldown visualizer (numeric + ring)
- [ ] In-game Add/Remove flow (currently EditorWindow only)
- [ ] Tutorial overlay
- [ ] Undo / redo
- [ ] Properties editable in runtime editor (not just EditorWindow)
- [ ] Save preset shortcut (Ctrl+S in runtime)
- [ ] EditMode test: edit timing → save → reload → assert value persisted

---

## 4. FSM Editor (F8)

Source: `python/src/roguelike_editors/fsm/` (159 files / 10 158 LOC, see `README_FSM.md`)
Target: `Scripts/Gameplay/Editors/FSMRuntimeEditor.cs` (655 LOC)

Core graph:

- [ ] Pan / zoom canvas (mouse wheel + middle-drag)
- [ ] Node rendering with state name + icon
- [ ] Edge rendering with arrows + condition label
- [ ] Live state highlight (current state pulses)

Toolbar (10 tools — see `toolbar_graph_panel_view.py`):

- [ ] Select tool
- [ ] Add Node tool
- [ ] Clone Node tool
- [ ] Connect tool
- [ ] Disconnect tool
- [ ] Delete tool
- [ ] Mark Initial tool
- [ ] Mark End tool
- [ ] Zoom In / Zoom Out buttons
- [ ] Hover yellow + selection blink (visual parity)

Side panels:

- [ ] Per-node properties panel
- [ ] Transition condition editor (visual builder for `when` / `from` / `to`)
- [ ] Animation map binding UI (see `anim_bridge.py`)
- [ ] Allowed-state-classes editor (`allowed_state_classes`, `allow_death`, `allow_damage`, `allow_unconscious`)
- [ ] Per-state tab strip (Attack / Idle / Damage / Death / Channel / Cooldown …)

Persistence:

- [ ] FSM-set save / load JSON round-trip
- [ ] Validate set on save (no orphan nodes, init/end marked, no dangling edges)

UX:

- [ ] Tutorial overlay
- [ ] Undo / redo
- [ ] EditMode test: build minimal set → save → reload → assert nodes/edges
- [ ] PlayMode test: assign set to NPC → observe transitions

---

## 5. Spawner Editor (F3)

Source: `python/src/roguelike_editors/spawner/` (167 files / 13 699 LOC — largest editor)
Target: `Scripts/Gameplay/Spawners/SpawnerEditorManager.cs` (~600 LOC)

Wave editor:

- [ ] Add/Remove wave entries
- [ ] Per-wave: prototype, count, spread_radius, kind (monster/etc.)
- [ ] Reorder waves drag handle
- [ ] Save to `spawner_waves.json`

Trigger editor:

- [ ] Type selector (proximity / auto / manual)
- [ ] Proximity radius slider (tile units)
- [ ] `auto_start` toggle
- [ ] `proximity_initial_only` toggle (mixed mode)
- [ ] `between_waves_cooldown_s` field

Policy editor:

- [ ] `loop` / `repeat` / `restart_on_done` toggles
- [ ] `max_active` integer
- [ ] `advance_on` dropdown (clear / cooldown)
- [ ] `count_ko_as_clear` toggle
- [ ] `cooldown_s`, `restart_cooldown_s`

Defend block:

- [ ] `defend_spawn` toggle
- [ ] `defend_leash` toggle
- [ ] Radius (tiles) — number / "random"
- [ ] Shape (circle / square)

Visuals block (per state):

- [ ] State → building instance dropdown (`state_visuals`)
- [ ] Per-state offset px (`visuals_offsets_px`)
- [ ] Per-state FX preset (`visuals_fx`)

Life block (per state — see `spawner_damage_system.py`):

- [ ] `hp_scope` dropdown (per_state / shared)
- [ ] `damageable` toggle
- [ ] `max_hp`, `flash_on_hit`, `flash_color`, `flash_duration_s`
- [ ] `hp_reset_on_enter` (set_to_max / keep_ratio / no_change)
- [ ] `sources` whitelist
- [ ] `next_step_by_hp` expression
- [ ] `end_logic` toggle

FSM-set:

- [ ] FSM-set picker per spawner
- [ ] Override params editor

Auto-repair:

- [ ] Trigger button: run `auto_repair_state_visuals` on selected
- [ ] Trigger button: run preflight validator on all spawners

UX:

- [ ] Tutorial overlay
- [ ] Wave debug overlay toggle (per spawner)
- [ ] Undo / redo
- [ ] Persistence to `spawners_instances.json`
- [ ] EditMode test: build wave → trigger → assert SpawnRequest issued

---

## 6. Tile Editor (F6)

Source: `python/src/roguelike_editors/tiles/` (51 files / 4 657 LOC)
Target: `Scripts/Gameplay/TileEditor/` (15 files / ~2 500 LOC) — strongest editor today.

- [ ] Brush sizes: 1×1, 3×3, 5×5
- [ ] Eyedropper tool
- [ ] Fill bucket
- [ ] Rectangle tool
- [ ] Line tool
- [ ] Layer toggles (ground / decals / walls top / walls bottom / objects high / low)
- [ ] Per-zone filter
- [ ] Auto-tile rules editor
- [ ] Undo / redo
- [ ] Tutorial overlay

---

## 7. Map Editor

Source: `python/src/roguelike_editors/map/` (65 files / 3 596 LOC)
Target: `Scripts/Gameplay/MapEditor/` (9 files / ~1 800 LOC)

- [ ] Editable zone offsets (drag handles)
- [ ] Portal placement + linker (source → destination zone)
- [ ] Region naming UI
- [ ] In-editor minimap preview
- [ ] Bulk zone operations (clone, shift, rotate)
- [ ] Undo / redo
- [ ] Save to world JSON

---

## 8. Items Editor (F7)

Source: `python/src/roguelike_editors/items/` (56 files / 4 243 LOC)
Target: `Scripts/Gameplay/Editors/ItemsRuntimeEditor.cs` (283 LOC)

- [ ] Editable name / icon / type / stackable / max_stack
- [ ] Per-type sub-tabs (food / weapon / shield / potion / consumable / quest)
- [ ] Add new item via in-game wizard
- [ ] Drag-drop into vendor inventories
- [ ] Price editor (buy / sell)
- [ ] Asset thumbnail with preview
- [ ] Undo / redo
- [ ] Save to ScriptableObject + items.json

---

## 9. Inventory Editor (F11)

Source: `python/src/roguelike_editors/inventory/` (87 files / 3 602 LOC)
Target: `Scripts/Gameplay/Editors/InventoryRuntimeEditor.cs` (228 LOC)

- [ ] Side selector (default / active player) — see `editor_view.py`
- [ ] Category tabs: player / monsters / hostile
- [ ] Editable slot grid with drag-and-drop
- [ ] Item selection panel (right pane)
- [ ] Save default vs Save active buttons
- [ ] Show default vs Show active toggle
- [ ] Live ECS sync of player inventory while editing
- [ ] Add item flow (wizard with quantity input)
- [ ] Tutorial overlay
- [ ] Undo / redo

---

## 10. Particles Editor (F9)

Source: `python/src/roguelike_editors/particles/` (44 files / 2 914 LOC)
Target: `Scripts/Gameplay/Editors/ParticlesRuntimeEditor.cs` + `Scripts/Editor/ParticlesEditorWindow.cs`

- [ ] Per-emitter parameter editor (rate, lifetime, speed, gravity, drag)
- [ ] Color gradient editor
- [ ] Curve editor (size / alpha over lifetime)
- [ ] Live preview viewport (isolated, not on player)
- [ ] Drag preset to spell / building
- [ ] Tutorial overlay
- [ ] Undo / redo

---

## 11. Lighting Editor (F10)

Source: `python/src/roguelike_editors/lighting/` (23 files / 2 037 LOC)
Target: `Scripts/Gameplay/Editors/LightingRuntimeEditor.cs` (504 LOC)

- [ ] Per-torch color / intensity / radius editor
- [ ] Ambient gradient editor (day/night curve)
- [ ] Light placement tool (click to add, drag to move)
- [ ] Save lighting profile to JSON
- [ ] Tutorial overlay
- [ ] Replace reflection-based Light2D access (audit Apr 2026 finding H2)

---

## Cross-cutting infrastructure (prerequisite for all editors)

Create a shared runtime UI widget kit in `Scripts/UI/EditorKit/`:

- [ ] `UndoStack<T>` generic (push / pop / redo / clear, capped)
- [ ] `EditorModal` component (backdrop + dismiss + result callback)
- [ ] `AssetThumbnailGrid` component (virtualized scrolling, selection)
- [ ] `TabStrip` component (horizontal / vertical, content slot)
- [ ] `TutorialOverlay` component (markdown-rendered help, toggle key)
- [ ] `PropertyForm` component (reflection-driven from ScriptableObject)
- [ ] `SearchBox` component
- [ ] EditMode test rig that opens each editor headless

Once this kit exists, every editor checklist above becomes mostly composition rather than from-scratch UI code.
