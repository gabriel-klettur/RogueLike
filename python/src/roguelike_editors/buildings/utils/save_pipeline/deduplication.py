"""Deduplication helpers for instance payloads."""
from __future__ import annotations

from typing import Dict, List, Tuple

__all__ = ["deduplicate"]

InstanceKey = str


def deduplicate(instances: List[dict]) -> Tuple[List[dict], int]:
    """Remove duplicate instances preferring spawn-linked entries.

    Returns the deduplicated instances and the amount of removed entries.
    """

    if not instances:
        return [], 0

    seen: Dict[InstanceKey, dict] = {}

    def _key(entry: dict) -> InstanceKey:
        try:
            zone = entry.get("zone")
            rel_x = int(entry.get("rel_x") or 0)
            rel_y = int(entry.get("rel_y") or 0)
            template_id = int(entry.get("template_id") or -1)
            return f"{zone}|{rel_x}|{rel_y}|{template_id}"
        except Exception:
            return str(id(entry))

    def _score(entry: dict) -> Tuple[int, int]:
        has_spawn = 1 if entry.get("spawn_id") is not None else 0
        try:
            neg_id = -int(entry.get("id") or 0)
        except Exception:
            neg_id = 0
        return has_spawn, neg_id

    for inst in instances:
        key = _key(inst)
        current = seen.get(key)
        if current is None:
            seen[key] = inst
            continue
        if _score(inst) > _score(current):
            seen[key] = inst

    result = list(seen.values())
    return result, len(instances) - len(result)
