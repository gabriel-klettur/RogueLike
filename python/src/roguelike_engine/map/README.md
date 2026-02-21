# Map package architecture

This package provides data models, services, and views to build, render, and persist
tile-based maps organized by zones and layers.

## Public API

Prefer importing from the top-level package:

```python
from roguelike_engine.map import (
    Map, Layer,
    MapService, build_map,
    MapView, ChunkedMapView,
    load_layers, save_layers,
    expand_dungeon,
    map_utils,  # legacy helpers namespace
)
```

Overlay persistence supports a multi-layer JSON format with a top-level `layers` object,
stored per zone under `data/map/zones/overlays/<zone>.overlay.json`.

## Modules

- model/
  - `map_model.Map`: immutable container with `matrix`, `layers`, `tiles_by_layer`, `metadata`, `name`.
    - Legacy: `overlay` (alias to `Layer.Ground`) and `tiles` (combined grid) are preserved.
  - `layer.Layer`: z-ordered enum for map rendering. Keep numeric values stable.
- controller/
  - `map_service.MapService`: builds world maps (lobby + dungeon + additional zones), merges zones, connects tunnels.
  - `map_controller.build_map`: convenience wrapper around a default `MapService` instance.
- view/
  - `map_view.MapView`: orchestrates zone rendering via `ZoneView`.
  - `chunked_map_view.ChunkedMapView`: chunked rendering with scaled sprite caching.
- model/overlay/
  - `overlay_manager.load_layers/save_layers`: multi-layer aware overlay IO.
  - `json_store.JsonOverlayStore`: per-zone JSON storage.
- services/
  - `expansion_service.expand_dungeon`: runtime expansion helper.
- helpers/
  - `geometry`: `intersect`, `center_of`.
  - `zones`: `get_zone_for_tile`.
  - `placement`: `generate_lobby_matrix`, `find_lobby_exit`, `calculate_lobby_offset`, `calculate_dungeon_offset`.

## Notes

- Backwards-compatibility: `roguelike_engine.map.utils` remains available and mirrors helpers.
- Editors (Map/Tiles/Entities) can depend on the stable public API above.
- The `Layer` enum numeric values define draw order and should not change casually (affects caches/persistence).
