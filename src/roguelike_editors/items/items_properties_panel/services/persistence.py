from __future__ import annotations

import os
import re
import json
import logging
from typing import Any, Dict, Tuple

from roguelike_ui.services.json_persistence import save_to_json, load_from_json
from roguelike_editors.items.services.items_repository import (
    all_items_dict as db_all_items_dict,
    upsert_entry as db_upsert_entry,
    update_field as db_update_field,
    save_asset_field as db_save_asset_field,
    rename_item_id as db_rename_item_id,
)

logger = logging.getLogger(__name__)


# ---------------------------- Paths ----------------------------

def get_items_json_path() -> str:
    """Return absolute path to items.json based on working directory."""
    return os.path.join(os.getcwd(), "data", "items", "items.json")


# ------------------------- Load/Save ops ------------------------

def load_items_data(path: str | None = None) -> Dict[str, Any]:
    # Ignore path; load from DB repository
    try:
        return db_all_items_dict() or {}
    except Exception:
        logger.exception("[ItemsPersistence] Failed to load items from DB")
        return {}


def save_field(path: str, item_id: str, key: str, value: Any) -> None:
    # Persist field directly to DB
    db_update_field(item_id, key, value)


def save_asset_field(path: str, item_id: str, key: str, value: Any) -> None:
    """Save an asset field, preserving list vs scalar semantics (DB-backed)."""
    db_save_asset_field(item_id, key, value)


def save_entry(path: str, item_id: str, entry: Dict[str, Any]) -> None:
    # Upsert full entry into DB
    db_upsert_entry(entry)


# ------------------------- Rename ID ops ------------------------

def rename_item_id(path: str, old_id: str, new_id: str) -> Tuple[bool, str]:
    """Rename an item id in DB (preserving data and price). Returns (ok, msg)."""
    try:
        return db_rename_item_id(old_id, new_id)
    except Exception as e:
        logger.exception(f"[ItemsPersistence] Failed to rename id {old_id}->{new_id}: {e}")
        return False, "exception"


# -------------------- Validation/Normalization ------------------

_ALLOWED_KEYS = {
    'id','name','description','stackable','max_stack','icon','icon_small','icon_large',
    'threshold','experience','effect','equip_slot','durability','damage','attack_speed',
    'range','crit_chance','crit_multiplier','weight','value','rarity','level_requirement',
    'quest_id','scale_editor','scale_map','scale_inventory','z_layer','default_params'
}

_ALLOWED_DEFAULT_PARAMS = {
    'dest_map','dest_x','dest_y','healing','mana','energy','hunger',
    'buff_stat','buff_value','duration','key_id','event_id'
}

_ID_PATTERN = re.compile(r'^[a-z0-9_]+$')


def validate_and_normalize_entry(entry: Dict[str, Any]) -> Tuple[bool, Dict[str, Any] | None]:
    """Validate and normalize an item entry to conform with the expected schema.

    - Filters unknown keys.
    - Removes empty values (None, "", [], {}).
    - Validates id pattern.
    - Ensures defaults for missing common fields.
    - Normalizes max_stack and default_params.
    Returns (ok, normalized_entry|None).
    """
    if 'id' not in entry or not isinstance(entry['id'], str) or not entry['id'].strip():
        return False, None
    eid = entry['id'] = entry['id'].strip()
    # Filter unknown keys
    entry = {k: v for k, v in entry.items() if k in _ALLOWED_KEYS}
    # Remove empties
    entry = {k: v for k, v in entry.items() if v not in (None, "", [], {})}
    # Validate id format
    if not _ID_PATTERN.fullmatch(eid):
        return False, None
    # Defaults
    entry.setdefault('name', eid)
    entry.setdefault('description', "")
    entry.setdefault('stackable', False)
    # Icons: require icon OR (icon_small AND icon_large)
    has_icon = bool(entry.get('icon'))
    has_both_sizes = bool(entry.get('icon_small')) and bool(entry.get('icon_large'))
    if not (has_icon or has_both_sizes):
        return False, None
    # Normalize max_stack
    if 'max_stack' in entry:
        try:
            if isinstance(entry['max_stack'], bool):
                entry.pop('max_stack', None)
            elif not isinstance(entry['max_stack'], int) or entry['max_stack'] < 1:
                entry.pop('max_stack', None)
        except Exception:
            entry.pop('max_stack', None)
    if entry.get('stackable') is True and 'max_stack' not in entry:
        entry['max_stack'] = 1
    # default_params
    if 'default_params' in entry and isinstance(entry['default_params'], dict):
        entry['default_params'] = {k: v for k, v in entry['default_params'].items() if k in _ALLOWED_DEFAULT_PARAMS}
    return True, entry
