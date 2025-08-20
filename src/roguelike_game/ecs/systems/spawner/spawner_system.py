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
            max_active = int(policy.get('max_active', 0) or 0)
            advance_on = str(policy.get('advance_on', 'clear') or 'clear').lower()
            advance_on_cooldown = (advance_on == 'cooldown')

            # Handle finished: either stop or auto-restart if looping is enabled
            if getattr(st, 'finished', False):
                if looping:
                    # Count down restart delay, then reset
                    if getattr(st, 'restart_cooldown_remaining', 0) > 0:
                        st.restart_cooldown_remaining -= 1
                        continue
                    st.finished = False
                    st.current_wave_idx = 0
                    st.spawned_this_wave = False
                    st.expected_this_wave = 0
                    try:
                        st.current_wave_entities.clear()
                    except Exception:
                        st.current_wave_entities = set()
                    # No extra delay: first wave spawns immediately after restart cooldown
                    st.cooldown_remaining = 0
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

            # Prune active_entities as well (used for max_active enforcement)
            if getattr(st, 'active_entities', None) is not None:
                active_alive = set()
                ents = set(world.entities)
                for ent_id in list(st.active_entities):
                    if ent_id in ents:
                        active_alive.add(ent_id)
                st.active_entities = active_alive

            # If we already spawned this wave and we advance on clear, wait until all are eliminated
            if st.spawned_this_wave and not advance_on_cooldown:
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
                            st.spawned_this_wave = False
                            st.expected_this_wave = 0
                            st.restart_cooldown_remaining = getattr(cfg, 'restart_cooldown_frames', getattr(cfg, 'cooldown_frames', 0))
                            st.finished = True
                            logger.info(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                            continue
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

            # In cooldown-advance mode: if we already spawned all waves, wait until all active entities are cleared to finish/restart
            if advance_on_cooldown and st.current_wave_idx >= len(cfg.waves):
                active_count = 0
                try:
                    active_count = len(getattr(st, 'active_entities', set()) or [])
                except Exception:
                    active_count = 0
                if active_count == 0:
                    if looping:
                        st.restart_cooldown_remaining = getattr(cfg, 'restart_cooldown_frames', getattr(cfg, 'cooldown_frames', 0))
                        st.finished = True
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                    else:
                        st.finished = True
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                # Either way, do nothing else this tick while waiting
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
                        st.spawned_this_wave = False
                        st.expected_this_wave = 0
                        st.restart_cooldown_remaining = getattr(cfg, 'restart_cooldown_frames', getattr(cfg, 'cooldown_frames', 0))
                        st.finished = True
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
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

            # Enforce max_active across waves if configured
            capacity_left = None
            if max_active > 0 and getattr(st, 'active_entities', None) is not None:
                try:
                    capacity_left = max(0, max_active - len(st.active_entities))
                except Exception:
                    capacity_left = max_active
            
            if capacity_left is not None and capacity_left <= 0:
                # No capacity: retry after cooldown
                st.cooldown_remaining = max(st.cooldown_remaining, getattr(cfg, 'cooldown_frames', 0))
                # Do not mark wave as spawned; try again later
                continue

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
                # Determine placement mode based on template-level spawn_radius
                sr = getattr(cfg, 'spawn_radius', None)
                shape = str(getattr(cfg, 'spawner_shape', 'circle') or 'circle').lower()
                random_mode = False
                random_radius = 0
                if isinstance(sr, (int, float)):
                    random_radius = int(sr)
                    random_mode = random_radius > 0
                elif isinstance(sr, str):
                    s = sr.strip().lower()
                    if s in {"random", "aleatorio", "aleatoreo"}:
                        random_mode = True
                        random_radius = max(1, int(fallback_max))
                # Apply capacity limit if present
                if capacity_left is not None:
                    if capacity_left <= 0:
                        continue
                    count = max(0, min(count, capacity_left))
                attempted_total += count
                for _ in range(count):
                    chosen = None
                    ax, ay = cfg.anchor_tile
                    # Try random-in-area first if enabled
                    if random_mode:
                        # Heuristic attempts: proportional to area; circle ~0.785 of square area
                        square_area = (2 * random_radius + 1) * (2 * random_radius + 1)
                        approx_area = square_area if shape == 'square' else max(1, int(square_area * 0.6))
                        attempts = max(25, min(200, approx_area))
                        for _try in range(attempts):
                            dx = random.randint(-random_radius, random_radius)
                            dy = random.randint(-random_radius, random_radius)
                            if shape != 'square':
                                # default circle
                                if dx*dx + dy*dy > random_radius * random_radius:
                                    continue
                            tx = ax + dx
                            ty = ay + dy
                            if (tx, ty) in solid or (tx, ty) in building:
                                continue
                            if (tx, ty) in npc_tiles or (tx, ty) in reserved_tiles or (tx, ty) in reserved_global:
                                continue
                            if map_manager and hasattr(map_manager, 'is_walkable'):
                                try:
                                    if not map_manager.is_walkable(tx, ty):
                                        continue
                                except Exception:
                                    pass
                            if min_px_dist > 0:
                                cx = tx * TILE_SIZE + TILE_SIZE // 2
                                cy = ty * TILE_SIZE + TILE_SIZE // 2
                                too_close = False
                                for ntx, nty in npc_tiles:
                                    nx = ntx * TILE_SIZE + TILE_SIZE // 2
                                    ny = nty * TILE_SIZE + TILE_SIZE // 2
                                    ddx = cx - nx
                                    ddy = cy - ny
                                    if ddx*ddx + ddy*ddy < min_px_dist_sq:
                                        too_close = True
                                        break
                                if too_close:
                                    continue
                                for rtx, rty in reserved_tiles.union(reserved_global):
                                    rx = rtx * TILE_SIZE + TILE_SIZE // 2
                                    ry = rty * TILE_SIZE + TILE_SIZE // 2
                                    ddx = cx - rx
                                    ddy = cy - ry
                                    if ddx*ddx + ddy*ddy < min_px_dist_sq:
                                        too_close = True
                                        break
                                if too_close:
                                    continue
                            chosen = (tx, ty)
                            break
                    # If no random choice found (or random disabled), fall back to center-first spiral
                    if chosen is None:
                        for tx, ty in self._iter_spiral_tiles(ax, ay, fallback_max):
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
                                    dx2 = cx - nx
                                    dy2 = cy - ny
                                    if dx2*dx2 + dy2*dy2 < min_px_dist_sq:
                                        too_close = True
                                        break
                                if too_close:
                                    continue
                                # Against already reserved tiles (this wave or globally)
                                for rtx, rty in reserved_tiles.union(reserved_global):
                                    rx = rtx * TILE_SIZE + TILE_SIZE // 2
                                    ry = rty * TILE_SIZE + TILE_SIZE // 2
                                    dx2 = cx - rx
                                    dy2 = cy - ry
                                    if dx2*dx2 + dy2*dy2 < min_px_dist_sq:
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
                    # Build optional defend area metadata
                    defend_center = None
                    defend_radius_px = None
                    defend_leash = None
                    defend_shape = None
                    try:
                        if getattr(cfg, 'defend_spawn', False):
                            # Use the same radius decision used for placement
                            defend_tiles = 0
                            if isinstance(sr, (int, float)) and int(sr) > 0:
                                defend_tiles = int(sr)
                            elif isinstance(sr, str) and str(sr).strip().lower() in {"random", "aleatorio", "aleatoreo"}:
                                defend_tiles = max(1, int(fallback_max))
                            if defend_tiles > 0:
                                ax, ay = cfg.anchor_tile
                                cx = ax * TILE_SIZE + TILE_SIZE // 2
                                cy = ay * TILE_SIZE + TILE_SIZE // 2
                                defend_center = (float(cx), float(cy))
                                defend_radius_px = float(defend_tiles * TILE_SIZE)
                                # Leash flag from config (default True)
                                defend_leash = bool(getattr(cfg, 'defend_leash', True))
                                # Shape mirrors spawner shape (circle|square)
                                defend_shape = str(shape)
                    except Exception:
                        defend_center = None
                        defend_radius_px = None
                        defend_leash = None
                        defend_shape = None

                    comps['SpawnRequest'][req_eid] = SpawnRequest(
                        prototype=proto,
                        position=chosen,
                        spawner_eid=eid,
                        wave_idx=st.current_wave_idx,
                        defend_center=defend_center,
                        defend_radius_px=defend_radius_px,
                        defend_leash=defend_leash,
                        defend_shape=defend_shape,
                    )
                    total_expected += 1
                    if capacity_left is not None:
                        capacity_left = max(0, capacity_left - 1)

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
                        st.spawned_this_wave = False
                        st.expected_this_wave = 0
                        st.restart_cooldown_remaining = getattr(cfg, 'restart_cooldown_frames', getattr(cfg, 'cooldown_frames', 0))
                        st.finished = True
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                    else:
                        st.finished = True
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                else:
                    st.cooldown_remaining = cfg.cooldown_frames
            else:
                # After issuing requests
                st.cooldown_remaining = cfg.cooldown_frames
                if advance_on_cooldown:
                    # Immediately move to next wave; completion will be checked after all waves are spawned
                    st.current_wave_idx += 1
                    st.spawned_this_wave = False
                    # expected_this_wave is not used for advancing in this mode
