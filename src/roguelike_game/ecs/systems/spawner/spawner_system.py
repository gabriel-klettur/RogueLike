"""
SpawnerRuntimeSystem: schedules spawn requests based on SpawnerConfig/State.
MVP supports:
- trigger: proximity (driven by SpawnerTriggerSystem setting state.started)
- policy: periodic with cooldown_s -> cooldown_frames
- waves[0].spawns: list of entries { kind: "monster", id: str, count: int, spread_radius: int }
"""
from __future__ import annotations

import random
from typing import Optional, Tuple

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest


class SpawnerRuntimeSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def _collect_blocked_tiles(self, world):
        solid_coords = {(t.rect.x // TILE_SIZE, t.rect.y // TILE_SIZE) for t in world.map_manager.solid_tiles}
        building_coords = {(r.x // TILE_SIZE, r.y // TILE_SIZE) for b in world.buildings for r in getattr(b, 'collision_tiles', [])}
        return solid_coords, building_coords

    def _find_local_spot(self, center: Tuple[int, int], radius: int, solid, building, attempts: int = 25) -> Optional[Tuple[int, int]]:
        cx, cy = center
        for _ in range(attempts):
            tx = cx + random.randint(-radius, radius)
            ty = cy + random.randint(-radius, radius)
            if (tx, ty) in solid or (tx, ty) in building:
                continue
            return tx, ty
        return None

    def update(self, world, camera=None):
        comps = world.components
        # Gather blocked tiles once
        solid, building = self._collect_blocked_tiles(world)

        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]

            # Only run if started and there is at least one wave
            if not st.started or not cfg.waves:
                continue

            # Cooldown handling
            if st.cooldown_remaining > 0:
                st.cooldown_remaining -= 1
                continue

            wave = cfg.waves[min(st.current_wave_idx, len(cfg.waves) - 1)]
            spawns = wave.get('spawns', [])
            if not spawns:
                # Nothing to spawn, reset cooldown to avoid tight loop
                st.cooldown_remaining = cfg.cooldown_frames
                continue

            # Issue spawn requests
            for entry in spawns:
                if entry.get('kind') != 'monster':
                    # MVP: only monsters
                    continue
                proto = entry.get('id', 'barbol')
                count = int(entry.get('count', 1))
                spread = int(entry.get('spread_radius', 3))
                for _ in range(count):
                    spot = self._find_local_spot(cfg.anchor_tile, spread, solid, building)
                    if spot is None:
                        continue
                    tx, ty = spot
                    req_eid = world.create_entity()
                    comps['SpawnRequest'][req_eid] = SpawnRequest(prototype=proto, position=(tx, ty))

            # Reset cooldown after batch
            st.cooldown_remaining = cfg.cooldown_frames
