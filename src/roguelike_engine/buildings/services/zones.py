from __future__ import annotations
from typing import Optional
import logging

# Sentinel names that should not warn when zone is missing
NO_ZONE_NAMES = {"no zone", "no-zone"}

logger = logging.getLogger("buildings.zones")

def normalize_zone(zone: Optional[str], offsets: dict[str, tuple[int, int]]) -> Optional[str]:
    """
    Returns a normalized key for the given zone using case-insensitive match
    against the provided offsets mapping. If zone is None, returns None.
    """
    if zone is None:
        return None
    if zone in offsets:
        return zone
    low = zone.lower()
    if low in offsets:
        return low
    for k in offsets.keys():
        if k.lower() == low:
            return k
    return zone


def zone_offset(
    zone: Optional[str],
    offsets: dict[str, tuple[int, int]],
    *,
    warn_context: Optional[str] = None,
) -> tuple[int, int]:
    """
    Returns (ox, oy) for the normalized zone.
    Emits a warning only if the zone does not exist and is not the 'no zone' sentinel.
    warn_context can be: None | "x_set" | "y_set" for tailored messages.
    """
    z_key = normalize_zone(zone, offsets)
    ox, oy = offsets.get(z_key, (0, 0))
    if z_key not in offsets:
        z_str = (zone or "")
        if z_str and z_str.lower() not in NO_ZONE_NAMES:
            if warn_context == "x_set":
                logger.warning(
                    "[BuildingModel] Zone '%s' not found in offsets when setting x. Using (0,0).",
                    zone,
                )
            elif warn_context == "y_set":
                logger.warning(
                    "[BuildingModel] Zone '%s' not found in offsets when setting y. Using (0,0).",
                    zone,
                )
            else:
                logger.warning(
                    "[BuildingModel] Zone '%s' not found in offsets. Using (0,0).",
                    zone,
                )
    return ox, oy
