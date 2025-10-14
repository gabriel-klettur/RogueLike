from __future__ import annotations

import logging
from typing import Any, Set, Tuple

from .spawner_cache import SpawnCaches
from .spawner_visuals import SpawnerVisualSync
from .spawner_wave import process_spawner

logger = logging.getLogger(__name__)

Tile = Tuple[int, int]


class SpawnerRuntimeSystem:
    """Thin orchestrator for spawner runtime.

    Delegates visual sync, placement caches, and wave FSM to specialized modules.
    Keeps API compatible with the original system (same class name and update signature).
    """

    def __init__(self, perf_log: Any | None = None, blocked_ttl_frames: int = 6) -> None:
        self.perf_log = perf_log
        self.caches = SpawnCaches(blocked_ttl_frames=blocked_ttl_frames)
        self.visuals = SpawnerVisualSync()

    def update(self, world, camera=None) -> None:
        # Advance frame and collect per-frame caches
        self.caches.next_frame()
        comps = world.components
        solid, building = self.caches.collect_blocked(world)
        # Global reservation set to avoid cross-spawner overlaps within the frame
        reserved_global: Set[Tile] = set()
        # Snapshot alive entity ids once
        try:
            ents_set = set(world.entities)
        except Exception:
            ents_set = set()

        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]
            # Visual state sync per frame
            self.visuals.sync(world, eid, cfg, st, self.caches.frame_idx)
            # Wave/requests processing per spawner
            process_spawner(
                world=world,
                eid=eid,
                cfg=cfg,
                st=st,
                solid=solid,
                building=building,
                caches=self.caches,
                ents_set=ents_set,
                reserved_global=reserved_global,
            )
