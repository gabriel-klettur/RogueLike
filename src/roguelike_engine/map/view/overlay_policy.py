"""Helpers to determine how overlays should be rendered for a map."""
from __future__ import annotations

import json
import logging
from pathlib import Path

from roguelike_engine.config.map_config import global_map_settings

logger = logging.getLogger(__name__)


def resolve_overlay_policy() -> bool:
    """Return ``True`` when the current world must rely exclusively on overlays.

    The original implementation performed this decision inline in several
    methods. Centralising the logic keeps those call-sites slimmer while
    retaining the defensive fallbacks built over time.
    """
    overlay_only = _is_blank_world()
    if overlay_only:
        return True

    overlay_only = _zones_configuration_is_empty()
    if overlay_only:
        return True

    return _overlays_directory_is_effectively_empty()


def _is_blank_world() -> bool:
    try:
        checker = getattr(global_map_settings, "is_blank_world", None)
        if checker is None:
            return False
        return bool(checker())
    except Exception:  # pragma: no cover - defensive against runtime configs
        logger.debug("Blank world detection failed; assuming non-blank world.", exc_info=True)
        return False


def _zones_configuration_is_empty() -> bool:
    zones_path = getattr(global_map_settings, "ZONES_INDEX", None)
    if not zones_path:
        return True

    try:
        if not zones_path.exists():
            return True
        text = zones_path.read_text(encoding="utf-8").strip()
    except Exception:  # pragma: no cover - IO errors default to overlay mode
        logger.debug("Unable to read ZONES_INDEX; assuming empty zones.", exc_info=True)
        return True

    if not text:
        return True

    try:
        data = json.loads(text)
    except json.JSONDecodeError:
        logger.debug("Invalid JSON in ZONES_INDEX; treating as non-empty.")
        return False

    return isinstance(data, dict) and len(data) == 0


def _overlays_directory_is_effectively_empty() -> bool:
    directory = getattr(global_map_settings, "overlays_dir", None)
    if not directory:
        return True

    try:
        files = list(Path(directory).glob("*.overlay.json"))
    except Exception:  # pragma: no cover - defensive for invalid paths
        logger.debug("Failed to enumerate overlays directory; assuming empty.", exc_info=True)
        return True

    if not files:
        return True

    normalized = {
        (stem[:-8] if stem.endswith(".overlay") else stem)
        for stem in (f.stem.lower().replace("_", " ") for f in files)
    }
    return normalized.issubset({"no zone", "no-zone"})
