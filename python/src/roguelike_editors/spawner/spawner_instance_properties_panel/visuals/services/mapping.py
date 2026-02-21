from __future__ import annotations

from typing import Any, Dict
import logging

logger = logging.getLogger(__name__)


def resolve_json_key_for_state(controller, state_key: str) -> str:
    """Resolve the actual JSON key used for a display state key (TitleCase)."""
    try:
        key_map = getattr(controller.parent.model, 'visuals_key_map', {}) or {}
        return str(key_map.get(state_key, state_key))
    except (AttributeError, TypeError, KeyError):
        return str(state_key)


def get_mapping_entry_for_state(controller, state_key: str):
    """Get raw mapping value from model.visuals for the given state (dict | int | None)."""
    try:
        visuals = getattr(controller.parent.model, 'visuals', {}) or {}
    except (AttributeError, TypeError):
        logger.debug("mapping.get_mapping_entry_for_state: failed to read visuals mapping", exc_info=True)
        visuals = {}
    try:
        return visuals.get(resolve_json_key_for_state(controller, state_key))
    except (AttributeError, TypeError, KeyError):
        logger.debug("mapping.get_mapping_entry_for_state: error accessing mapping", exc_info=True)
        return None


def get_instance_id_for_state(controller, state_key: str) -> int | None:
    """Extract instance_id as int from the mapping of a state, if present and valid."""
    raw = get_mapping_entry_for_state(controller, state_key)
    try:
        if raw is None:
            return None
        if isinstance(raw, dict):
            return int(raw.get('instance_id') or raw.get('id') or raw.get('building_instance_id'))
        return int(raw)
    except (ValueError, TypeError, AttributeError, KeyError):
        return None


def get_template_id_for_state(controller, state_key: str) -> int | None:
    """Best-effort template_id resolution for a state: prefer explicit mapping, fallback to building index."""
    raw = get_mapping_entry_for_state(controller, state_key)
    try:
        if isinstance(raw, dict) and raw.get('template_id') is not None:
            return int(raw.get('template_id'))
    except (ValueError, TypeError, AttributeError, KeyError):
        logger.debug("mapping.get_template_id_for_state: failed to read template_id from mapping", exc_info=True)
    # Fallback via buildings index if instance_id present
    try:
        bid = get_instance_id_for_state(controller, state_key)
        idx = getattr(controller.parent, '_building_index', {}) or {}
        if bid is not None and int(bid) in idx:
            tid_str = idx.get(int(bid))
            return int(tid_str) if tid_str is not None else None
    except (AttributeError, TypeError, ValueError, KeyError):
        logger.debug("mapping.get_template_id_for_state: failed to fallback via building index", exc_info=True)
    return None
