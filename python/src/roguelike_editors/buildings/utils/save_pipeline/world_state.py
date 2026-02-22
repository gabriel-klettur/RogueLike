"""Helpers related to the world configuration when saving buildings."""
from __future__ import annotations

import json
import logging

from roguelike_engine.config.map_config import global_map_settings

logger = logging.getLogger(__name__)


def is_blank_world() -> bool:
    """Return True when the current world has no zones configured.

    Matches legacy behavior: if zones file is missing or unreadable, we DO NOT blank
    instances (return False). Only an explicitly empty JSON ({} or empty text) is
    considered a blank world.
    """

    try:
        try:
            content = global_map_settings.ZONES_INDEX.read_text(encoding="utf-8-sig")
        except Exception:
            content = global_map_settings.ZONES_INDEX.read_text(encoding="utf-8")
        zones_text = (content or "").strip()
        return (not zones_text) or (json.loads(zones_text) == {})
    except Exception:
        # Legacy fallback: any error -> treat as non-blank to avoid data loss in tests/tools
        return False
