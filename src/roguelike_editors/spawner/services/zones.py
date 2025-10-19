from __future__ import annotations

from typing import Optional
import logging

from roguelike_engine.config.map_config import global_map_settings

logger = logging.getLogger(__name__)


def zone_for_global_tile(tx: int, ty: int) -> Optional[str]:
    """Return the zone name that contains the global tile (tx, ty), or None.

    Uses `global_map_settings.zone_offsets` and zone_size.
    """
    try:
        w, h = global_map_settings.zone_size
        for name, (ox, oy) in global_map_settings.zone_offsets.items():
            # Skip sentinel entries
            if name in ('no zone', 'no-zone'):
                continue
            if ox <= tx < ox + w and oy <= ty < oy + h:
                return name
    except (AttributeError, KeyError, TypeError, ValueError):
        logger.debug("zone_for_global_tile: failed while computing zone", exc_info=True)
    return None
