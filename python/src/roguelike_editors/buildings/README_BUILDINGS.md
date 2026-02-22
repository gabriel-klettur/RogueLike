# Buildings Editor

This document describes the in‑game Buildings Editor: purpose, features, controls, architecture, and how its panels fit together.

## Overview
- The Buildings Editor lets you select, move, resize, split, reorder (Z-layer), delete and place building instances in the world.
- It includes an asset picker to browse `assets/buildings/` and a specialized Colliders Panel to edit/building colliders.
- The editor integrates with the game camera for panning/zoom and persists changes to JSON.

## Key Features
- __Selection & Active building__: hover to cycle between overlapped buildings; the “active building” renders outline and tool handles.
- __Move__: right-click drag an instance. Drops on mouse up. Zone/relatives are reassigned after drag.
- __Resize__: press `R` to start resize on the hovered building; release `R` (KeyUp) to finish.
- __Split bar__: drag the split handle to adjust `split_ratio` for the active building.
- __Z-layer buttons__: plus/minus style buttons around the active building to move it on top/bottom layers.
- __Reset__: reset handle on the active building to restore default size/ratio.
- __Delete__: red delete handle or `Delete` key; supports undo of deletions (`Ctrl+Z`).
- __Collider scope toggle__: bottom-right CG/CU toggle per active building to switch collider edit scope (global by `image_path` vs unique instance).
- __Asset Picker__: browse folders and images with thumbnails, quick back icon, scrollable grid, RMB drag-and-drop to place, RMB drag the panel itself.
- __Add/Remove Panel__: quick actions aligned to the right of the toolbar (open picker, add/remove actions, etc.).
- __Colliders Panel__: when active, takes over collider editing; building tool overlays are visually suppressed.
- __UI Blocking__: hovering/clicking over any UI panel suppresses building hover/active visuals and prevents accidental edits.

## Controls & Shortcuts
- __Toggle Editor__: `F10`
  - Opens the Buildings Editor (select mode). Picker is hidden initially.
  - Closing saves buildings automatically.
- __Open/Close Picker__: `P`
- __Cancel / Close__:
  - In editor: `Esc` closes the editor and saves.
  - In picker: `Esc` cancels drag if any; otherwise closes the picker.
- __Reset hovered building__: `D`
- __Resize__: `R` to start on hovered; release `R` to finish.
- __Place random building (no picker)__: `N`
- __Delete building under mouse__: `Delete`
- __Undo delete__: `Ctrl+Z`
- __Save__: `Ctrl+S`
- __Camera pan__: Middle mouse button drag
- __Cycle hovered when multiple overlap__: Mouse wheel
- __Mouse interactions__:
  - LMB: tool handles (reset, resize, split, collider-scope, z-layer buttons), picker grid selection.
  - RMB: drag building in world; in picker, RMB starts asset drag and RMB drop places it; RMB on picker background drags the picker panel.

## Architecture
- __Manager__: `src/roguelike_game/managers/editors/buildings_editor_manager.py`
  - Wires MVC components, specialized panels, and toolbar.
  - Exposes `handle()`, `update()`, `render()` for the game loop.
  - Important: does not clear UI blockers mid-frame; blockers are cleared once per frame by the renderer manager.
- __MVC Core__ (Buildings Editor):
  - __Model__: `building_editor_model.py`
    - State flags (`active`, `dragging`, `resizing`, `split_dragging`), hover list/index, active tool (`select`), picker state, collider scope (`CG`/`CU`), `colliders_mode` flag when the Colliders Panel is active, and panel dragging offsets.
  - __Controller__: `building_editor_controller.py`
    - Tool orchestration: `ResizeTool`, `DefaultTool`, `SplitTool`, `ZTool` (top/bottom), `ColliderScopeTool`, `PlacerTool`, `DeleteTool`.
    - Mouse down/up/motion handling, prevents edits when UI panels block the pointer, assigns zones on drop, updates sizes/positions, and integrates the Building Picker controller.
  - __View__: `building_editor_view.py`
    - Renders title bar, picker panel, outlines/handles over the active building, z-layer buttons, split handle, collider scope toggle.
    - Suppresses building overlays if the mouse is over any UI blocker.
  - __Events__: `building_editor_events.py`
    - Central event router. Delegates to Toolbar, Add/Remove Panel, Colliders Panel, and Picker.
    - Keyboard shortcuts, camera panning, hover cycling, persistence on quit and on saves.
- __Specialized Panels__:
  - __Toolbar Panel__ (`buildings_tool_bar_panel/`)
    - Tools: `buildings_manager` (opens picker + add/remove), `buildings_colliders` (toggles colliders panel), `undo`, `redo` (placeholder).
    - View: `buildings_tool_bar_panel_view.py` (centers below title; flashes colliders icon when active).
    - Events: `buildings_tool_bar_panel_events.py` (toggles panels, calls editor undo; redo pending).
  - __Add/Remove Panel__ (`buildings_add_remove_panel/`)
    - View aligns to the right of the toolbar. Publishes its rect so the Picker can align next to it.
    - Icons: `add_building`, `remove_building`, `add_building_on_system`.
  - __Colliders Panel__ (`buildings_colliders_panel/`)
    - Own MVC and events. When active sets `editor_state.colliders_mode = True` which hides other tool overlays and routes events to this panel.
  - __Title Bar__ (`buildings_title_panel/buildings_title_view.py`)
    - Reusable `TitleBar` widget. Registers a UI blocker.
  - __Building Picker__ (`buildings_picker/`)
    - Controller lists `entries` for folders/images, manages directory navigation history, and asset drag/drop.
    - View draws a grid with back icon, folder thumbnails with labels, image thumbnails with cache, scroll bar, and path label.
    - Events handle LMB selection, RMB start-drag or panel drag, mouse wheel scroll, scroll thumb drag, and RMB drop to place.

## Data & Persistence
- __Save paths (split)__: `data/buildings/buildings_templates.json` + `data/buildings/buildings_instances.json`
- __Save method__: `roguelike_editors.buildings.utils.save_buildings_to_json.save_buildings_split`
- __When saved__:
  - On `pygame.QUIT` if the editor is active.
  - On `F10` when closing the editor.
  - On `Ctrl+S`.
  - After mouse up following drag/resize/split operations.
- __Zone integration__: on mouse up the controller calls `assign_zone_and_relatives(building)` so position changes update zone-relative data.
- __Z-state & zones__: `save_buildings_split` receives `z_state` and `zone_offsets` (from `roguelike_engine.config.map_config.global_map_settings.zone_offsets`).

## UI Blockers & Interaction Safety
- UI panels call `roguelike_ui.ui_blocker.register_blocker(rect)` (e.g., Title and Picker). The editor checks `is_blocked(mx, my)` to:
  - Avoid processing building clicks when over a panel (`building_editor_controller.on_mouse_down`).
  - Suppress hover/active outlines while over panels (`building_editor_view.render`).
  - Clear hovered/active state and early-return on motion (`building_editor_events.py`).
- This prevents “hover bleed-through” when multiple editors/panels are visible.

## Camera Integration
- Coordinates transform with `camera.zoom`, `camera.offset_x`, `camera.offset_y`.
- Panning with MMB adjusts offsets proportionally to zoom.

## Extending the Editor
- __Add a new tool overlay__: follow the pattern in `src/roguelike_editors/buildings/tools/*` and inject it in `BuildingEditorController` and `BuildingEditorView`.
- __Toolbar additions__: add a key to `BuildingsToolBarPanelModel.tools`, provide an icon in `assets/ui/`, handle clicks in `buildings_tool_bar_panel_events.py`.
- __Picker customization__: tweak constants in `buildings_editor_config.py` (thumb size, padding, colors, back icon).

## File Map (Main)
- `building_editor_model.py` — editor state
- `building_editor_controller.py` — tool orchestration + mouse logic
- `building_editor_view.py` — outlines/handles + picker + title
- `building_editor_events.py` — central event handler
- `buildings_editor_config.py` — visual constants
- `buildings_picker/*` — picker MVC
- `buildings_colliders_panel/*` — colliders editor MVC
- `buildings_add_remove_panel/*` — add/remove actions panel
- `buildings_tool_bar_panel/*` — toolbar MVC
- `buildings_title_panel/*` — title view
- `src/roguelike_game/managers/editors/buildings_editor_manager.py` — manager/wiring

## Known limitations / notes
- Toolbar `redo` is a placeholder.
- Deletion redo stack is not implemented; only simple undo of deletions is available.
- When the Colliders Panel is active, building tool overlays are hidden (`colliders_mode=True`).

## Quick Start
1) Run the game and press `F10` to open the Buildings Editor.
2) Click the buildings manager icon on the toolbar to open the picker (or press `P`).
3) Right-click an asset in the picker to start a drag, then right-click drop on the map to place it.
4) Hover a building and:
   - Press `R` to resize (release to finish).
   - Use on-screen handles: reset, split bar, z-layer buttons, CG/CU toggle.
   - Right-drag to move; press `Delete` to remove; `Ctrl+Z` to undo a deletion.
5) Press `Esc` to close the editor and save, or `Ctrl+S` to save changes anytime.
