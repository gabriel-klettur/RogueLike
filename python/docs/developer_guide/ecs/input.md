# Input System Architecture

This document describes how input is captured, filtered, and dispatched across layers.

- Global capture and filtering: `src/roguelike_game/managers/core/events.py::handle_events(game)`
  - Captures pygame events once per frame and handles high-priority cases:
    - Quit, ESC, menu toggles, Class Selector.
    - Editor toggles: Spawner (F3), Spells (F4), Entities (F5), Inventory (F6), Items (F7), Tiles, Buildings, Map.
    - Diagnostics overlay consumption and UI blocking using `roguelike_ui.ui_blocker.is_blocked`.
    - Spawner Editor exception: allow MMB passthrough over UI (for its own panning/drag) while visible.
  - Forwards only the remaining events to the engine input layer.

- Engine input routing: `src/roguelike_engine/input/events.py::handle_events(...)`
  - Pre-runs editor handlers if active (tiles/buildings/map).
  - Routes keyboard events to `input/keyboard.py` (stub; global shortcuts live in managers/core).
  - Routes mouse events to `input/mouse.py` always.
  - MMB camera panning is enabled only while an editor is active (tiles/buildings/map) or the Spawner Editor is visible. In gameplay it is disabled.

- Mouse specifics: `src/roguelike_engine/input/mouse.py::handle_mouse(...)`
  - Mouse wheel zoom.
  - MMB panning start/move/end when `mmb_pan_enabled` is True.
  - Cancels panning immediately if the context disables it mid-pan.
  - Right click gameplay actions are handled by the ECS InputSystem; legacy right-click is ignored here.

- Gameplay input (polling): `src/roguelike_game/ecs/systems/input/input_system.py`
  - Polls keyboard/mouse state each frame and maps to InputComponent (move, attack, spells).
  - Applies context suppression (item editor, class selector, spawner editor, UI drag) and respects UI blockers.

## Principles

- Single source of truth for global shortcuts: managers/core/events.
- Engine input layer only routes device events and implements generic camera/zoom behavior.
- Editors handle their own interactions and consume events so gameplay doesn\'t double-handle.
- ECS polls continuous gameplay input and drives FSM and actions.

## MMB Panning Policy

- Disabled in gameplay so MMB remains available for gameplay actions (e.g., laser beam via `InputSystem`).
- Enabled while tiles/buildings/map editors are active, and also when the Spawner Editor is visible (those contexts may require camera panning).
- Spawner Editor UI passthrough: managers/core allows MMB passthrough over UI while Spawner is visible so camera panning works when dragging on the game world; MMB on top of Spawner UI panels may be consumed and not reach the engine.

## Testing Guidance

- Verify overlay consumes wheel/click on its panel and events don't reach the engine layer.
- Verify UI blocking prevents mouse click/wheel from reaching gameplay except MMB passthrough for Spawner Editor.
- Verify ECS freezes gameplay input while Item Editor or Class Selector is open.
- Verify MMB panning:
  - Does NOT start in gameplay.
  - Starts while editors are active.
  - Cancels immediately if editors become inactive mid-pan.
