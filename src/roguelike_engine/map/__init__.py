"""
Public API for the map module.

This package provides a cohesive set of tools to build, render, and persist
tile-based maps organized by zones and layers.

Key concepts:
- Map model (`Map`) with text matrix, per-layer codes, and per-layer tiles.
- Layer enum (`Layer`) defining draw order and semantic meaning.
- Services to build procedural/loaded maps (`MapService`, `build_map`).
- Rendering helpers (`MapView`, `ChunkedMapView`).
- Overlay persistence per zone with multi-layer JSON format (`load_layers`, `save_layers`).
- Runtime expansion helpers (`expand_dungeon`).

The public surface mirrors existing internal usage for backward compatibility
while promoting clear imports in application code and editors.
"""

# Models
from .model.map_model import Map
from .model.layer import Layer

# Services / Controllers
from .controller.map_service import MapService
from .controller.map_controller import build_map

# Views
from .view.map_view import MapView
from .view.chunked_map_view import ChunkedMapView

# Overlay persistence (multi-layer aware)
from .model.overlay.overlay_manager import (
    load_overlay,
    save_overlay,
    load_layers,
    save_layers,
)

# Runtime utilities/services
from .services.expansion_service import expand_dungeon

# Common utility helpers (aliases kept for convenience)
from . import utils as map_utils
from .utils import (
    intersect,
    center_of,
    find_closest_room_center,
    get_zone_for_tile,
    generate_lobby_matrix,
    find_lobby_exit,
    calculate_lobby_offset,
    calculate_dungeon_offset,
)

__all__ = [
    # Models
    "Map",
    "Layer",
    # Services / Controllers
    "MapService",
    "build_map",
    # Views
    "MapView",
    "ChunkedMapView",
    # Overlay
    "load_overlay",
    "save_overlay",
    "load_layers",
    "save_layers",
    # Runtime
    "expand_dungeon",
    # Utils
    "map_utils",
    "intersect",
    "center_of",
    "find_closest_room_center",
    "get_zone_for_tile",
    "generate_lobby_matrix",
    "find_lobby_exit",
    "calculate_lobby_offset",
    "calculate_dungeon_offset",
]