"""Filtering helpers for buildings to persist."""
from __future__ import annotations

from typing import Iterable, List, Optional, Set, Tuple

__all__ = ["collect_persistable", "get_spawn_id"]


def collect_persistable(buildings: Iterable[object]) -> Tuple[List[object], int]:
    """Return buildings eligible for persistence plus skipped spawner count."""

    seen_spawn_ids: Set[str] = set()
    skipped_visuals = 0
    persistable: List[object] = []

    for building in buildings:
        if _is_spawner_visual(building):
            skipped_visuals += 1
            continue
        spawn_id = get_spawn_id(building)
        if spawn_id is not None:
            if spawn_id in seen_spawn_ids:
                continue
            seen_spawn_ids.add(spawn_id)
        persistable.append(building)

    return persistable, skipped_visuals


def _is_spawner_visual(building: object) -> bool:
    try:
        root_flag = getattr(building, "_is_spawner_visual", False)
        spawner_attr = getattr(building, "_spawner_eid", None)
        return bool(root_flag or spawner_attr is not None)
    except Exception:
        return False


def get_spawn_id(building: object) -> Optional[str]:
    try:
        spawn_id = getattr(building, "spawn_id", None) or getattr(building, "spawner_instance_id", None)
        if spawn_id is None:
            return None
        return str(spawn_id)
    except Exception:
        return None
