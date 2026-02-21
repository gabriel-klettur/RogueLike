from __future__ import annotations

from typing import Any, Dict
import logging

from .persistence import load_items_data

logger = logging.getLogger(__name__)


def ensure_schema(model: Any) -> None:
    """Populate model.schema_keys and model.schema_types based on items.json if missing.

    - schema_keys: ordered list of discovered keys, with preferred ordering first.
    - schema_types: simplified type hints per key (bool,int,float,str,dict,list -> class).
    """
    if getattr(model, 'schema_keys', None):
        return
    try:
        data = load_items_data()
    except Exception:  # pragma: no cover - defensive
        data = {}
    keys_set = set()
    type_map: Dict[str, type] = {}
    for entry in (data or {}).values():
        if not isinstance(entry, dict):
            continue
        for k, v in entry.items():
            keys_set.add(k)
            if v is None:
                continue
            t = type(v)
            prev = type_map.get(k)
            if prev is None or (prev is str and t is not str):
                type_map[k] = t
    preferred = ["id", "name", "description", "icon", "icon_small", "icon_large"]
    ordered = [k for k in preferred if k in keys_set]
    for k in sorted(keys_set):
        if k not in ordered:
            ordered.append(k)
    model.schema_keys = ordered
    simple: Dict[str, type] = {}
    for k, t in type_map.items():
        if t in (bool, int, float, str, dict, list):
            simple[k] = t
        else:
            simple[k] = str
    setattr(model, 'schema_types', simple)
