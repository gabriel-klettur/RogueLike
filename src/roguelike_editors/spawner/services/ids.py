from __future__ import annotations

from typing import Any, Dict, List, Tuple, Set
import re
import uuid


def slugify(s: str) -> str:
    s = str(s)
    s = s.strip().lower()
    s = re.sub(r"[^a-z0-9]+", "_", s)
    s = re.sub(r"_+", "_", s)
    return s.strip('_')


def generate_instance_id(inst: Dict[str, Any], existing_ids: Set[str]) -> str:
    tpl = slugify(inst.get('template_id', 'tpl'))
    zone = slugify(inst.get('zone', 'zone'))
    try:
        tile = inst.get('tile', [0, 0])
        x, y = int(tile[0]), int(tile[1])
    except (KeyError, TypeError, ValueError, AttributeError):
        x, y = 0, 0
    base = f"{tpl}_{zone}_{x}_{y}" if tpl or zone else f"inst_{x}_{y}"
    if not base:
        base = f"inst_{uuid.uuid4().hex[:8]}"
    candidate = base
    i = 1
    while candidate in existing_ids:
        i += 1
        candidate = f"{base}_{i}"
    return candidate


def ensure_instance_ids(data: List[Dict[str, Any]]) -> Tuple[bool, List[Dict[str, Any]]]:
    """Ensure each instance dict has a unique 'id' (string). Returns (changed, data)."""
    changed = False
    ids: Set[str] = set()
    # First pass: normalize and collect
    for inst in data:
        cur = inst.get('id')
        if cur is not None:
            try:
                s = str(cur).strip()
            except Exception:
                s = ""
            if s:
                # ensure uniqueness
                if s in ids:
                    # will regenerate in second pass
                    inst['id'] = None  # type: ignore
                    changed = True
                else:
                    inst['id'] = s
                    ids.add(s)
            else:
                inst.pop('id', None)
                changed = True
    # Second pass: generate for missing or duplicated
    for inst in data:
        if not inst.get('id'):
            new_id = generate_instance_id(inst, ids)
            inst['id'] = new_id
            ids.add(new_id)
            changed = True
    return changed, data
