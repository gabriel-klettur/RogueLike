from __future__ import annotations

import logging
import time
from typing import Any

logger = logging.getLogger(__name__)


class TimedDespawnSystem:
    """System that removes entities once their TimedDespawn TTL expires."""

    def __init__(self, perf_log: Any | None = None) -> None:
        self.perf_log = perf_log

    def update(self, world, *args):  # noqa: ANN001
        comps = world.components
        store = comps.get("TimedDespawn", {}) or {}
        if not store:
            return

        now = time.time()
        to_remove: list[int] = []
        for eid, comp in list(store.items()):
            try:
                start = float(getattr(comp, "start_time", now))
                ttl = float(getattr(comp, "ttl", 0.0))
            except Exception:
                continue
            if ttl <= 0:
                continue
            if (now - start) >= ttl:
                to_remove.append(eid)

        for eid in to_remove:
            try:
                store.pop(eid, None)
                world.remove_entity(eid)
            except Exception:
                continue
        if to_remove:
            try:
                logger.info("[TimedDespawnSystem] Removed %s entities (TTL expired)", len(to_remove))
            except Exception:
                pass
