# Tile Picker Panel Tests

This directory contains `pytest` test suites for the `tiles_picker_panel` module:

## test_tile_picker_state.py
- Verifies default `TilePickerState` values:
  - `open`, `current_choice`, `scroll_offset`, `pos`, `dragging`, etc.
  - Rects and flags for tileset and config UI elements
  - Text input default values and grid size parsing

## test_tile_picker_events.py
- Exercises `TilePickerEventHandler` click logic:
  - Closed vs open state handling
  - Surface bounds checking
  - Toolbar button toggling and close action
  - Tileset filter checkbox click and asset reload
  - Tileset input activation
  - Tileset creation click: slicing paths, resetting state, updating controller
  - Drag start handling (right button)
  - Grid click handling: file vs filter mode, single vs double click branching
  - `handle_event` routing: drag motion, stop drag, scroll, text input events

## test_tile_picker_controller.py
- Validates `TilePickerController` core behavior:
  - Initialization sets `base_dir`, `current_dir`, and `view`
  - `swap_positions`: list and view update, JSON save stubbed
  - `is_over`: mouse-over picker bounds
  - `drag` and `stop_drag` update picker_state correctly
  - `scroll` adjusts `editor_state.scroll_offset`
  - `open` and `_close`: resets state, calls `_load_assets` and `_load_positions`

## test_tile_picker_view.py
- Covers `TilePickerView` rendering helpers:
  - Initialization creates fonts, text input, and panel placeholder
  - `_ellipsize` truncates and adds ellipsis correctly
  - `_compute_layout` uses `ScrollableGrid` for dimensions
  - `_get_local_coords` calculates local mouse position and y-offset
  - `render` no-op when picker is closed

---

Run tests with:

```bash
pytest tests/roguelike_editors/tiles/tiles_picker_panel
```
