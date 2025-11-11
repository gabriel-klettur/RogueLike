"""Helpers related to the world configuration when saving buildings."""
from __future__ import annotations

import json
import logging

from roguelike_engine.config.map_config import global_map_settings

logger = logging.getLogger(__name__)


def is_blank_world() -> bool:
    """Return True when the current world has no zones configured."""

    try:
        try:
            content = global_map_settings.ZONES_INDEX.read_text(encoding="utf-8-sig")
        except Exception:
            content = global_map_settings.ZONES_INDEX.read_text(encoding="utf-8")
        zones_text = (content or "").strip()
        return (not zones_text) or (json.loads(zones_text) == {})
    except FileNotFoundError:
        logger.info("[Buildings][SaveSplit] zones.json missing; treating as blank world.")
        return True
    except Exception:
        return False
