"""Placement utilities for zones and coordinates."""
from __future__ import annotations

from typing import Optional, Tuple

from roguelike_editors.buildings.utils.zones import canonicalize_zone


def extract_position(building: object) -> Tuple[Optional[str], int, int]:
    """Return zone identifier and relative position for persistence."""

    zone = canonicalize_zone(getattr(building, "zone", None))
    rel_x = int(getattr(building, "rel_x", 0))
    rel_y = int(getattr(building, "rel_y", 0))
    return zone, rel_x, rel_y
