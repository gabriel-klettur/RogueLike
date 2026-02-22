from __future__ import annotations

from typing import Any, Dict, Optional

from .item_data import get_item_data


def select_key_to_edit(data: Dict[str, Any], explicit_key: Optional[str]) -> Optional[str]:
    """Choose which property to edit in inline edit.

    Priority: explicit -> name -> description -> first non-None key.
    """
    key_to_edit = explicit_key
    if key_to_edit is None:
        for candidate in ("name", "description"):
            if candidate in data:
                key_to_edit = candidate
                break
        if key_to_edit is None:
            for k, v in data.items():
                if v is None:
                    continue
                key_to_edit = k
                break
    return key_to_edit


def get_initial_text(item: Any, key: str) -> str:
    """Return initial text for TextInput based on item attribute value."""
    try:
        return str(getattr(item, key, ""))
    except Exception:
        return ""
