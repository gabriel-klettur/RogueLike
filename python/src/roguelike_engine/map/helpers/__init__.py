"""Domain-focused helper modules for the map package.

- geometry: rect and center helpers
- zones: zone lookup helpers
- placement: lobby/dungeon placement helpers

Prefer importing from these modules in new code. The legacy `map.utils` keeps
backwards-compatibility and may internally forward to these helpers in the future.
"""

from .geometry import intersect, center_of
from .zones import get_zone_for_tile
from .placement import (
    generate_lobby_matrix,
    find_lobby_exit,
    calculate_lobby_offset,
    calculate_dungeon_offset,
)

__all__ = [
    "intersect",
    "center_of",
    "get_zone_for_tile",
    "generate_lobby_matrix",
    "find_lobby_exit",
    "calculate_lobby_offset",
    "calculate_dungeon_offset",
]
