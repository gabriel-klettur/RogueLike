# Tiles Collision Panel Tests

This directory contains `pytest` test suites covering the `tiles_collision_panel` module:

## test_tiles_collision_panel_states.py
- Verifies default `TilesCollisionPanelState` values:
  - `open` is `False`
  - `choice` is `None`
  - `option_rects` is an empty `dict`

## test_tiles_collision_panel_events.py
- Tests `TilesCollisionPanelEventHandler` event routing:
  - `_select_collision()` sets `collision_choice` and returns `True` when clicking an option
  - Left-click inside/outside panel consumes or ignores events correctly
  - Right-click starts or skips drag based on panel bounds
  - Mouse motion moves panel only when dragging
  - Right-button release stops drag appropriately

## test_tiles_collision_panel_controller.py
- Tests `TilesCollisionPanelController` functionality:
  - Initialization assigns controller and view correctly
  - `render()` delegates to `view.render()`
  - `apply_brush()`:
    - Skips when collisions are disabled or no choice is set
    - Adds/removes tile collisions correctly
    - Updates `game_map.matrix` and `solid_tiles` list
    - Records pending collision zones
    - Triggers `update_chunks()` on the view

## test_tiles_collision_panel_view.py
- Tests `TilesCollisionPanelView` rendering helpers:
  - Initialization creates options and panel
  - `_compute_dimensions()` calculates width/height based on `THUMB`, `PAD`, and font height
  - `_fallback_center()` places panel below `view_panel_controller` state or centers on screen
  - `_store_panel_state()` updates `toolbar_state` size
  - `_render_options()` populates `state.option_rects` with correct `pygame.Rect` positions


---

Run tests with:

```bash
pytest tests/roguelike_editors/tiles/tiles_collision_panel
```
