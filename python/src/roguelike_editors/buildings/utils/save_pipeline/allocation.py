"""Instance ID allocation helpers."""
from __future__ import annotations

import logging
from typing import Dict, Iterable, Optional

from .models import AllocationStats

__all__ = ["InstanceAllocator"]

logger = logging.getLogger(__name__)

PositionKey = str


def _make_position_key(zone: Optional[str], rel_x: int, rel_y: int, template_id: int) -> PositionKey:
    return f"{zone}|{rel_x}|{rel_y}|{template_id}"


def _push_sample(container: list, value, limit: int = 3) -> None:
    if len(container) < limit:
        container.append(value)


class InstanceAllocator:
    """Computes stable instance IDs across save operations."""

    def __init__(self, existing_instances: Iterable[dict]) -> None:
        self._by_spawn: Dict[str, int] = {}
        self._by_position: Dict[PositionKey, int] = {}
        self._max_id = 0
        self._used_ids: set[int] = set()
        for entry in existing_instances or []:
            try:
                iid_raw = entry.get("id") if isinstance(entry, dict) else None
                iid = int(iid_raw) if iid_raw is not None and str(iid_raw).isdigit() else None
            except Exception:
                iid = None
            if iid is None:
                continue
            self._max_id = max(self._max_id, iid)
            spawn_id = entry.get("spawn_id")
            if spawn_id is not None:
                self._by_spawn[str(spawn_id)] = iid
            key = _make_position_key(entry.get("zone"), int(entry.get("rel_x") or 0), int(entry.get("rel_y") or 0), int(entry.get("template_id") or -1))
            self._by_position[key] = iid

    def allocate(
        self,
        building: object,
        template_id: int,
        zone: Optional[str],
        rel_x: int,
        rel_y: int,
        spawn_id: Optional[str],
        stats: AllocationStats,
    ) -> int:
        """Return a stable instance ID while updating allocation statistics."""

        candidate = self._from_building(building)
        if candidate is not None:
            if candidate in self._used_ids:
                logger.warning(
                    "[Buildings][SaveSplit] Preserve-id conflict: id=%s already used in this pass; will reassign for building at zone=%s rel=(%s,%s) tpl=%s",
                    candidate,
                    zone,
                    rel_x,
                    rel_y,
                    template_id,
                )
            else:
                self._mark_used(candidate)
                stats.preserved_count += 1
                _push_sample(stats.preserved_samples, candidate)
                return candidate

        candidate = self._from_spawn(spawn_id)
        if candidate is not None and candidate not in self._used_ids:
            self._mark_used(candidate)
            stats.reused_spawn_count += 1
            _push_sample(stats.reused_spawn_samples, (spawn_id, candidate))
            return candidate

        candidate = self._from_position(zone, rel_x, rel_y, template_id)
        if candidate is not None and candidate not in self._used_ids:
            self._mark_used(candidate)
            key = _make_position_key(zone, rel_x, rel_y, template_id)
            stats.reused_position_count += 1
            _push_sample(stats.reused_position_samples, (key, candidate))
            return candidate

        new_id = self._next_id()
        self._mark_used(new_id)
        stats.new_assigned_count += 1
        _push_sample(stats.new_assigned_samples, new_id)
        logger.debug(
            "[Buildings][SaveSplit] New ID assigned: iid=%s zone=%s rel=(%s,%s) tpl=%s",
            new_id,
            zone,
            rel_x,
            rel_y,
            template_id,
        )
        return new_id

    def _mark_used(self, iid: int) -> None:
        self._used_ids.add(iid)
        self._max_id = max(self._max_id, iid)

    def _next_id(self) -> int:
        self._max_id += 1
        return self._max_id

    @staticmethod
    def _from_building(building: object) -> Optional[int]:
        try:
            current = getattr(building, "id", None)
            if current is None:
                return None
            return int(current) if str(current).isdigit() else None
        except Exception:
            return None

    def _from_spawn(self, spawn_id: Optional[str]) -> Optional[int]:
        if not spawn_id:
            return None
        return self._by_spawn.get(str(spawn_id))

    def _from_position(
        self,
        zone: Optional[str],
        rel_x: int,
        rel_y: int,
        template_id: int,
    ) -> Optional[int]:
        key = _make_position_key(zone, rel_x, rel_y, template_id)
        return self._by_position.get(key)
