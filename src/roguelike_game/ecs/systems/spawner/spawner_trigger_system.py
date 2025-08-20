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
        comps = world.components
        # Evaluate triggers per spawner
        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]
            trig = (cfg.trigger or {})
            ttype = trig.get('type', 'proximity')

            if ttype == 'auto':
                # Always started; future: support start_delay_s if needed
                st.started = True
                continue

            if ttype != 'proximity':
                # Unknown trigger types: keep current state
                continue

            # Proximity trigger
            player_tile = self._get_player_tile(world)
            if player_tile is None:
                # No player: can't evaluate proximity, default to not started unless persistent state
                continue
            px, py = player_tile
            radius = int(trig.get('radius', 5))
            ax, ay = cfg.anchor_tile
            dx = px - ax
            dy = py - ay
            within = (dx*dx + dy*dy) <= (radius * radius)

            auto = bool(trig.get('auto_start', True))
            if auto:
                st.started = within
            else:
                if within and not st.started:
                    # Reserved for future manual confirmation
                    pass
