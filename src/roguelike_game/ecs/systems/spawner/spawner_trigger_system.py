"""
SpawnerTriggerSystem: handles proximity triggers to start/stop spawners.
MVP: only 'proximity' trigger type with tile radius; optional auto_start.
"""
from __future__ import annotations

import math
from typing import Optional

from roguelike_engine.config.config_tiles import TILE_SIZE


class SpawnerTriggerSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def _get_player_tile(self, world) -> Optional[tuple[int, int]]:
        comps = world.components
        players = [eid for eid in comps.get('PlayerTagComponent', {})]
        if not players:
            return None
        eid = players[0]
        pos = comps.get('Position', {}).get(eid)
        if pos is None:
            return None
        return int(pos.x // TILE_SIZE), int(pos.y // TILE_SIZE)

    def update(self, world, camera=None):
        player_tile = self._get_player_tile(world)
        if player_tile is None:
            return
        px, py = player_tile

        comps = world.components
        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]

            if (cfg.trigger or {}).get('type') != 'proximity':
                continue
            radius = int((cfg.trigger or {}).get('radius', 5))
            ax, ay = cfg.anchor_tile
            dx = px - ax
            dy = py - ay
            # Euclidean distance in tiles
            within = (dx*dx + dy*dy) <= (radius * radius)

            auto = bool((cfg.trigger or {}).get('auto_start', True))
            if auto:
                st.started = within
            else:
                # If not auto, leave it off by default (will be toggled by future UI prompt)
                if within and not st.started:
                    # could raise a prompt event in the future
                    pass
