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
import logging

logger = logging.getLogger(__name__)


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

    def _iter_spiral_tiles(self, cx: int, cy: int, max_radius: int):
        """Generate tiles in spiral rings around (cx, cy), starting at center."""
        yield (cx, cy)
        for r in range(1, max_radius + 1):
            x0, x1 = cx - r, cx + r
            y0, y1 = cy - r, cy + r
            # top & bottom edges
            for x in range(x0, x1 + 1):
                yield (x, y0)
                yield (x, y1)
            # left & right edges (without corners to avoid duplicates)
            for y in range(y0 + 1, y1):
                yield (x0, y)
                yield (x1, y)

    def _collect_npc_tiles(self, world):
        """Return set of global tile coords occupied by existing NPCs/Player (alive)."""
        comps = world.components
        pos_map = comps.get('Position', {})
        multi_map = comps.get('MultiCollider', {})
        death_map = comps.get('DeathTimer', {})
        tiles = set()
        # Consider all entities with Position+MultiCollider (player and NPCs)
        for nid in world.get_entities_with('Position', 'MultiCollider'):
            if nid in death_map:
                continue
            p = pos_map.get(nid)
            if not p:
                continue
            tx = int(p.x // TILE_SIZE)
            ty = int(p.y // TILE_SIZE)
            tiles.add((tx, ty))
        return tiles

    def update(self, world, camera=None):
        comps = world.components
        # Gather blocked tiles once
        solid, building = self._collect_blocked_tiles(world)
        # Global reserved tiles for this tick to avoid cross-spawner overlaps
        reserved_global = set()
        # Map walkability helper
        map_manager = getattr(world, 'map_manager', None)

        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]
            policy = getattr(cfg, 'policy', {}) or {}
            looping = bool(policy.get('loop') or policy.get('repeat') or policy.get('restart_on_done'))

            # Handle finished: either stop or auto-restart if looping is enabled
            if getattr(st, 'finished', False):
                if looping:
                    st.finished = False
                    st.current_wave_idx = 0
                    st.spawned_this_wave = False
                    st.expected_this_wave = 0
                    try:
                        st.current_wave_entities.clear()
                    except Exception:
                        st.current_wave_entities = set()
                    st.cooldown_remaining = max(st.cooldown_remaining, getattr(cfg, 'cooldown_frames', 0))
                else:
                    continue

            # Only run if started and there is at least one wave
            if not st.started or not cfg.waves:
                continue

            # Always prune dead/missing entities from the current wave tracking
            if getattr(st, 'current_wave_entities', None) is not None:
                alive = set()
                ents = set(world.entities)
                for ent_id in list(st.current_wave_entities):
                    if ent_id in ents:
                        alive.add(ent_id)
                st.current_wave_entities = alive

            # If we already spawned this wave, wait until all are eliminated
            if st.spawned_this_wave:
                if st.expected_this_wave > 0 and len(st.current_wave_entities) == 0:
                    # Wave completed
                    wave_num = st.current_wave_idx + 1
                    total_waves = len(cfg.waves)
                    logger.info(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed")
                    st.current_wave_idx += 1
                    st.spawned_this_wave = False
                    st.expected_this_wave = 0
                    # If no more waves, either loop or mark finished
                    if st.current_wave_idx >= len(cfg.waves):
                        if looping:
                            st.current_wave_idx = 0
                            st.spawned_this_wave = False
                            st.expected_this_wave = 0
                            st.cooldown_remaining = max(st.cooldown_remaining, getattr(cfg, 'cooldown_frames', 0))
                            logger.info(f"[Spawner] {cfg.template_id}:{eid} loop restart")
                        else:
                            st.finished = True
                            logger.info(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                            continue
                    else:
                        # Small delay before next wave
                        st.cooldown_remaining = max(st.cooldown_remaining, getattr(cfg, 'cooldown_frames', 0))
                else:
                    # Still waiting for monsters to be eliminated or none actually spawned yet
                    continue

            # Cooldown handling (only matters when spawning a new wave)
            if st.cooldown_remaining > 0:
                st.cooldown_remaining -= 1
                continue

            # Determine current wave to spawn
            wave = cfg.waves[min(st.current_wave_idx, len(cfg.waves) - 1)]
            spawns = wave.get('spawns', [])
            if not spawns:
                # Nothing to spawn -> consider wave instantly completed and advance
                wave_num = st.current_wave_idx + 1
                total_waves = len(cfg.waves)
                logger.info(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed (empty)")
                st.current_wave_idx += 1
                if st.current_wave_idx >= len(cfg.waves):
                    if looping:
                        st.current_wave_idx = 0
                        st.spawned_this_wave = False
                        st.expected_this_wave = 0
                        st.cooldown_remaining = max(st.cooldown_remaining, getattr(cfg, 'cooldown_frames', 0))
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} loop restart")
                    else:
                        st.finished = True
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                else:
                    st.cooldown_remaining = cfg.cooldown_frames
                continue

            # Issue spawn requests for this wave (avoid overlaps via spiral search)
            # Reset tracking for this wave
            st.current_wave_entities.clear()
            total_expected = 0
            attempted_total = 0
            # Tiles currently occupied by actors
            npc_tiles = self._collect_npc_tiles(world)
            # Reserve tiles chosen in this wave to prevent duplicates
            reserved_tiles = set()
            for entry in spawns:
                if entry.get('kind') != 'monster':
                    # MVP: only monsters
                    continue
                proto = entry.get('id', 'barbol')
                count = int(entry.get('count', 1))
                spread = int(entry.get('spread_radius', 3))
                fallback_max = int(entry.get('spread_fallback_max', max(spread, 8)))
                min_px_dist = int(entry.get('min_px_distance', 0))
                min_px_dist_sq = min_px_dist * min_px_dist
                attempted_total += count
                for _ in range(count):
                    chosen = None
                    for tx, ty in self._iter_spiral_tiles(cfg.anchor_tile[0], cfg.anchor_tile[1], fallback_max):
                        if (tx, ty) in solid or (tx, ty) in building:
                            continue
                        if (tx, ty) in npc_tiles or (tx, ty) in reserved_tiles or (tx, ty) in reserved_global:
                            continue
                        if map_manager and hasattr(map_manager, 'is_walkable'):
                            try:
                                if not map_manager.is_walkable(tx, ty):
                                    continue
                            except Exception:
                                # If walkability fails, fall back to solid/building checks only
                                pass
                        if min_px_dist > 0:
                            # Candidate pixel center
                            cx = tx * TILE_SIZE + TILE_SIZE // 2
                            cy = ty * TILE_SIZE + TILE_SIZE // 2
                            too_close = False
                            # Against existing actors
                            for ntx, nty in npc_tiles:
                                nx = ntx * TILE_SIZE + TILE_SIZE // 2
                                ny = nty * TILE_SIZE + TILE_SIZE // 2
                                dx = cx - nx
                                dy = cy - ny
                                if dx*dx + dy*dy < min_px_dist_sq:
                                    too_close = True
                                    break
                            if too_close:
                                continue
                            # Against already reserved tiles (this wave or globally)
                            for rtx, rty in reserved_tiles.union(reserved_global):
                                rx = rtx * TILE_SIZE + TILE_SIZE // 2
                                ry = rty * TILE_SIZE + TILE_SIZE // 2
                                dx = cx - rx
                                dy = cy - ry
                                if dx*dx + dy*dy < min_px_dist_sq:
                                    too_close = True
                                    break
                            if too_close:
                                continue
                        chosen = (tx, ty)
                        break
                    if chosen is None:
                        continue
                    reserved_tiles.add(chosen)
                    reserved_global.add(chosen)
                    req_eid = world.create_entity()
                    comps['SpawnRequest'][req_eid] = SpawnRequest(
                        prototype=proto,
                        position=chosen,
                        spawner_eid=eid,
                        wave_idx=st.current_wave_idx,
                    )
                    total_expected += 1

            # Telemetry: placed vs attempted for this wave
            try:
                wave_num = st.current_wave_idx + 1
                total_waves = len(cfg.waves)
                logger.info(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} placed {total_expected}/{attempted_total} spawns")
            except Exception:
                pass

            # If nothing could be placed, we still consider the wave done immediately
            st.expected_this_wave = total_expected
            st.spawned_this_wave = True
            if total_expected == 0:
                # No entities actually spawned -> mark as completed immediately
                wave_num = st.current_wave_idx + 1
                total_waves = len(cfg.waves)
                logger.info(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed (no spots)")
                st.current_wave_idx += 1
                st.spawned_this_wave = False
                st.expected_this_wave = 0
                if st.current_wave_idx >= len(cfg.waves):
                    # End reached: loop or finish
                    policy = getattr(cfg, 'policy', {}) or {}
                    looping = bool(policy.get('loop') or policy.get('repeat') or policy.get('restart_on_done'))
                    if looping:
                        st.current_wave_idx = 0
                        st.spawned_this_wave = False
                        st.expected_this_wave = 0
                        st.cooldown_remaining = max(st.cooldown_remaining, getattr(cfg, 'cooldown_frames', 0))
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} loop restart")
                    else:
                        st.finished = True
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                else:
                    st.cooldown_remaining = cfg.cooldown_frames
            else:
                # After issuing requests, wait for completion
                st.cooldown_remaining = cfg.cooldown_frames
