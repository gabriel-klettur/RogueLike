from __future__ import annotations

from typing import Any, Dict, Tuple, Optional, Callable
from pathlib import Path
import logging

from roguelike_engine.config.config import ASSETS_DIR

logger = logging.getLogger(__name__)


def pick_icon_key_from_data(data: Dict[str, Any]) -> str:
    """Choose which icon-like key to update in an existing item data."""
    for k in ("icon", "icon_small", "icon_large"):
        if k in data:
            return k
    return "icon"


def pick_icon_key_from_schema(model: Any) -> str:
    """Choose icon-like key when editing a draft (no item yet)."""
    schema_keys = getattr(model, 'schema_keys', []) or []
    for k in ("icon", "icon_small", "icon_large"):
        if k in schema_keys:
            return k
    return "icon"


def normalize_asset_path(path: str) -> str:
    """Return a path relative to assets folder when possible."""
    try:
        rel = Path(path).resolve().relative_to(Path(ASSETS_DIR).resolve()).as_posix()
        return f"assets/{rel}"
    except Exception:
        return str(path)


def apply_asset_to_draft(model: Any, target_key: str, asset_value: str) -> None:
    """Apply chosen asset to a new item draft preserving list vs str semantics."""
    old_val = model.new_item_draft.get(target_key)
    if isinstance(old_val, list):
        if len(old_val) > 0:
            old_val[0] = asset_value
        else:
            model.new_item_draft[target_key] = [asset_value]
    else:
        model.new_item_draft[target_key] = asset_value


def apply_asset_to_item(item: Any, target_key: str, asset_value: str) -> None:
    """Apply chosen asset to an in-memory item preserving list vs str semantics."""
    old_val = getattr(item, target_key, None)
    if isinstance(old_val, list):
        if len(old_val) > 0:
            old_val[0] = asset_value
        else:
            setattr(item, target_key, [asset_value])
    else:
        try:
            setattr(item, target_key, asset_value)
        except Exception:
            try:
                setattr(item, target_key, asset_value)
            except Exception:
                pass
