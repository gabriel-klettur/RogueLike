# Unit Tests for Tile Editor

This directory contains pytest suites for the core Tile Editor modules:

## test_tile_editor_state.py
- Verifies default values of `TileEditorState` and functionality of `clone()`.

## test_tiles_editor_config.py
- Checks constants in `tiles_editor_config.py` (colors, dimensions, paths, tools list).

## test_tile_editor_controller.py
- Tests `TileEditorController` initialization and key methods:
  - Controller wiring of sub-controllers and outline view
  - `select_tile_at()`, `_tile_under_mouse()`, `_get_brush_cell()`, `start_brush()`

## test_tile_editor_events.py
- Tests `TileEditorEventHandler`:
  - Handles `QUIT` and `KEYDOWN` events (`ESC`, `F8`)
  - Brush start and flush on mouse button events

## test_tile_editor_view.py
- Tests `TileEditorView`:
  - Does not render when inactive
  - Renders tile outline when active

## test_tile_outline_view.py
- Tests `TileOutlineView`:
  - Draws hover and selection outlines correctly


### Running Tests

```bash
pytest tests/roguelike_editors/tiles
```
