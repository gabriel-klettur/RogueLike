from __future__ import annotations

from typing import Any, Dict


def get_item_data(item: Any) -> Dict[str, Any]:
    """Return a dict-like view of an item, using model_dump/dict/vars in that order."""
    if hasattr(item, 'model_dump'):
        return item.model_dump()
    try:
        return item.dict()  # type: ignore[attr-defined]
    except Exception:
        return vars(item)
