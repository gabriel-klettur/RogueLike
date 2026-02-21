from typing import Optional
from roguelike_engine.config.map_config import global_map_settings
import logging

logger = logging.getLogger(__name__)


def canonicalize_zone(zone: Optional[str]) -> Optional[str]:
    """Return canonical zone key present in global_map_settings.zone_offsets.

    Case-insensitive match. Keeps "no zone" sentinel as-is. Falls back to input
    while logging a warning if not found.
    """
    try:
        if not zone or not isinstance(zone, str):
            return zone
        if zone.lower() == "no zone":
            return "no zone"
        offsets = getattr(global_map_settings, "zone_offsets", {}) or {}
        if zone in offsets:
            return zone
        low = zone.lower()
        if low in ("lobby", "dungeon") and low in offsets:
            return low
        for k in offsets.keys():
            if k.lower() == low:
                return k
        logger.warning(
            "[Buildings] Zone '%s' not found in offsets (keys=%s). Using as-is; building may be misaligned.",
            zone,
            list(offsets.keys()),
        )
        return zone
    except Exception:
        return zone
