from __future__ import annotations

from typing import Optional, Tuple, Set
from .placement_utils import (
    collect_blocked_tiles as util_collect_blocked,
    collect_npc_tiles as util_collect_npcs,
)


class SpawnCaches:
    """Per-frame caches for blocked tiles and NPC tiles.

    Keeps small TTL for expensive scans to avoid redoing work every tick.
    """

    def __init__(self, blocked_ttl_frames: int = 6) -> None:
        self.frame_idx: int = 0
        self._blocked_cache_last: Optional[Tuple[Set, Set]] = None
        self._blocked_cache_updated_frame: int = -10_000
        self._blocked_cache_ttl_frames: int = int(blocked_ttl_frames)

        self._npc_tiles_cache: Optional[Set] = None
        self._npc_tiles_frame: int = -1

    def next_frame(self) -> None:
        self.frame_idx += 1
        # note: npc tiles are cached per-frame, so we'll recompute when asked on each frame

    def collect_blocked(self, world) -> Tuple[Set, Set]:
        if (
            self._blocked_cache_last is not None
            and (self.frame_idx - self._blocked_cache_updated_frame) < self._blocked_cache_ttl_frames
        ):
            return self._blocked_cache_last
        blocked = util_collect_blocked(world)
        self._blocked_cache_last = blocked
        self._blocked_cache_updated_frame = self.frame_idx
        return blocked

    def collect_npc_tiles(self, world) -> Set:
        if self._npc_tiles_frame == self.frame_idx and self._npc_tiles_cache is not None:
            return self._npc_tiles_cache
        tiles = util_collect_npcs(world)
        self._npc_tiles_cache = tiles
        self._npc_tiles_frame = self.frame_idx
        return tiles
