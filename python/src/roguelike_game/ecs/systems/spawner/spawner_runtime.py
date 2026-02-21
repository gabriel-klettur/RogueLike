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

    def __init__(self, perf_log: Any | None = None, blocked_ttl_frames: int = 30) -> None:
        self.perf_log = perf_log
        self._default_blocked_ttl = int(blocked_ttl_frames)
        self.caches = SpawnCaches(blocked_ttl_frames=blocked_ttl_frames)
        self.visuals = SpawnerVisualSync()

    def update(self, world, camera=None) -> None:
        # Advance frame and collect per-frame caches
        self.caches.next_frame()
        # If the Buildings Editor is active, reduce frequency of expensive blocked-tiles scans
        try:
            st = getattr(getattr(world, 'game', None), 'state', None)
            editor_active = bool(getattr(getattr(st, 'editor', None), 'active', False))
            if editor_active:
                # Bump TTL (e.g., ~1s at 60 FPS) to amortize heavy scans
                self.caches._blocked_cache_ttl_frames = max(self.caches._blocked_cache_ttl_frames, 60)
            else:
                # Restore default when editor is not active
                self.caches._blocked_cache_ttl_frames = max(1, int(self._default_blocked_ttl))
        except Exception:
            # Keep previous TTL if environment doesn't expose game/state/editor
            pass
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
            # Wave/requests processing per spawner (updates FSM state first)
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
            # Visual state sync after FSM state changes, to reflect the correct initial state in the same frame
            self.visuals.sync(world, eid, cfg, st, self.caches.frame_idx)
