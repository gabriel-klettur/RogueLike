"""
SpawnerRuntimeSystem: schedules spawn requests based on SpawnerConfig/State.
MVP supports:
- trigger: proximity (driven by SpawnerTriggerSystem setting state.started)
- policy: periodic with cooldown_s -> cooldown_frames
- waves[0].spawns: list of entries { kind: "monster", id: str, count: int, spread_radius: int }
"""
from __future__ import annotations

from roguelike_engine.config.config_tiles import TILE_SIZE
import roguelike_engine.config.config as config
from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest
import logging
from roguelike_game.ecs.systems.spawner.placement_utils import collect_blocked_tiles as util_collect_blocked, collect_npc_tiles as util_collect_npcs, choose_spawn_tile

logger = logging.getLogger(__name__)


class SpawnerRuntimeSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Cache of last applied building id per spawner eid to avoid redundant work
        self._last_visual_id: dict[int, int | None] = {}
        # Frame counter for lightweight per-tick caches
        self._frame_idx: int = 0
        # Blocked tiles cache (recompute every N frames to avoid scanning buildings each tick)
        self._blocked_cache_last: tuple[set, set] | None = None
        self._blocked_cache_updated_frame: int = -999999
        self._blocked_cache_ttl_frames: int = 6  # recompute roughly every 0.1s at 60 FPS
        # NPC tiles cache (once per frame)
        self._npc_tiles_cache: set | None = None
        self._npc_tiles_frame: int = -1

    # ---------------------- Visual helpers ----------------------
    def _current_state_key(self, st) -> str | None:
        """Return a canonical lowercase key for the current FSM-ish state in runtime.
        Values set in this system are like 'await_trigger', 'spawning_wave', 'wait_cooldown', 'wait_restart', 'finished'.
        """
        try:
            cur = getattr(st, 'fsm_state', None)
            return str(cur).strip().lower() if cur is not None else None
        except Exception:
            return None

    def _desired_building_for_state(self, cfg, st) -> int | None:
        """Resolve desired building_id for the current state using cfg.state_visuals if present,
        falling back to cfg.building_id.
        Accepts keys in cfg.state_visuals either as exact state ids (e.g., 'AwaitTrigger') or
        lowercase runtime tokens (e.g., 'await_trigger')."""
        # Fallback first
        desired = getattr(cfg, 'building_id', None)
        mapping = getattr(cfg, 'state_visuals', None)
        if not mapping:
            return desired
        # Normalize mapping keys once per lookup (small dict)
        norm: dict[str, int] = {}
        try:
            for k, v in mapping.items():
                try:
                    norm[str(k).strip().lower()] = int(v) if v is not None else None  # type: ignore
                except Exception:
                    norm[str(k).strip().lower()] = v  # type: ignore
        except Exception:
            norm = {}
        key = self._current_state_key(st)
        if key and key in norm:
            return norm[key]
        # Try mapping runtime tokens to set ids (title case without underscores)
        # e.g., 'await_trigger' -> 'awaittrigger', check also 'AwaitTrigger'
        if key:
            try:
                camel = ''.join(part.title() for part in key.split('_'))
                if camel.lower() in norm:
                    return norm[camel.lower()]
                # Direct title-case key
                if camel in mapping:
                    val = mapping[camel]
                    try:
                        return int(val) if val is not None else None
                    except Exception:
                        return val  # type: ignore
            except Exception:
                pass
        return desired

    def _find_linked_building(self, world, eid: int):
        blds = getattr(world, 'buildings', []) or []
        for ob in blds:
            try:
                if getattr(ob, '_spawner_eid', None) == eid:
                    return ob
            except Exception:
                continue
        return None

    def _find_building_by_id(self, world, building_id: int):
        if building_id is None:
            return None
        blds = getattr(world, 'buildings', []) or []
        for ob in blds:
            try:
                if getattr(ob, 'id', None) == building_id:
                    return ob
            except Exception:
                continue
        return None

    def _sync_spawner_visual(self, world, eid: int, cfg, st) -> None:
        """Ensure only the active state's visual building is runtime-visible for this spawner.
        Uses a 'runtime_hidden' flag on Building objects to let the renderer skip them.
        When cfg.visible_in_game is False, any previously linked or matching buildings are hidden.
        """
        # If visuals are disabled, hide any linked building for this spawner and return
        if not getattr(cfg, 'visible_in_game', False):
            try:
                for ob in getattr(world, 'buildings', []) or []:
                    try:
                        if getattr(ob, '_spawner_eid', None) == eid:
                            setattr(ob, 'runtime_hidden', True)
                    except Exception:
                        continue
            except Exception:
                pass
            self._last_visual_id[eid] = None
            return
        desired = self._desired_building_for_state(cfg, st)
        prev = self._last_visual_id.get(eid)
        if desired == prev:
            # Even if id didn't change, ensure exclusive visibility according to desired
            try:
                for ob in getattr(world, 'buildings', []) or []:
                    try:
                        if getattr(ob, '_spawner_eid', None) == eid:
                            setattr(ob, 'runtime_hidden', getattr(ob, 'id', None) != desired)
                    except Exception:
                        continue
            except Exception:
                pass
            return
        # Update linkage
        cur = self._find_linked_building(world, eid)
        if cur is not None and getattr(cur, 'id', None) != desired:
            # Detach previous link
            try:
                if getattr(cur, '_spawner_eid', None) == eid:
                    setattr(cur, '_spawner_eid', None)
                if getattr(cur, '_world_ref', None) is world:
                    setattr(cur, '_world_ref', None)
                if getattr(cur, '_is_spawner_visual', False):
                    setattr(cur, '_is_spawner_visual', False)
            except Exception:
                pass
            cur = None
        if desired is not None:
            target = cur if cur and getattr(cur, 'id', None) == desired else self._find_building_by_id(world, int(desired))
            if target is not None:
                try:
                    setattr(target, '_spawner_eid', eid)
                    setattr(target, '_world_ref', world)
                    setattr(target, '_is_spawner_visual', True)
                    # Make it visible at runtime and hide siblings for this spawner
                    setattr(target, 'runtime_hidden', False)
                except Exception:
                    pass
        # Hide any other building linked to this spawner eid (exclusive runtime visibility)
        try:
            for ob in getattr(world, 'buildings', []) or []:
                try:
                    if getattr(ob, '_spawner_eid', None) == eid and getattr(ob, 'id', None) != desired:
                        setattr(ob, 'runtime_hidden', True)
                except Exception:
                    continue
        except Exception:
            pass
        self._last_visual_id[eid] = desired

    def _collect_blocked_tiles(self, world):
        """Return (solid, building) blocked tiles using a short TTL cache to reduce work."""
        if (
            self._blocked_cache_last is not None
            and (self._frame_idx - self._blocked_cache_updated_frame) < self._blocked_cache_ttl_frames
        ):
            return self._blocked_cache_last
        blocked = util_collect_blocked(world)
        self._blocked_cache_last = blocked
        self._blocked_cache_updated_frame = self._frame_idx
        return blocked

    # Nota: La lógica de colocación (random/espiral, forma, radio, distancia mínima)
    # se ha extraído a placement_utils.py para mantener este sistema enfocado en
    # progresión de oleadas y emisión de SpawnRequest.

    def _collect_npc_tiles(self, world):
        """Return set of global tile coords occupied by existing NPCs/Player (alive).
        Cached once per frame across all spawners to avoid repeated scans.
        """
        if self._npc_tiles_frame == self._frame_idx and self._npc_tiles_cache is not None:
            return self._npc_tiles_cache
        tiles = util_collect_npcs(world)
        self._npc_tiles_cache = tiles
        self._npc_tiles_frame = self._frame_idx
        return tiles

    def _compute_defend_metadata(self, cfg, sr, fallback_max: int, shape: str):
        """Build optional defend area metadata coupled to spawner placement settings.
        Returns (defend_center, defend_radius_px, defend_leash, defend_shape).
        """
        defend_center = None
        defend_radius_px = None
        defend_leash = None
        defend_shape = None
        try:
            if getattr(cfg, 'defend_spawn', False):
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
                    defend_leash = bool(getattr(cfg, 'defend_leash', True))
                    defend_shape = str(shape)
        except Exception:
            defend_center = None
            defend_radius_px = None
            defend_leash = None
            defend_shape = None
        return defend_center, defend_radius_px, defend_leash, defend_shape

    # ---------------------- Helpers de legibilidad ----------------------
    def _get_policy_flags(self, cfg):
        """Extrae y normaliza flags de política del spawner.
        Devuelve: (looping, max_active, advance_on_cooldown, proximity_initial_only)
        """
        policy = getattr(cfg, 'policy', {}) or {}
        looping = bool(policy.get('loop') or policy.get('repeat') or policy.get('restart_on_done'))
        max_active = int(policy.get('max_active', 0) or 0)
        advance_on = str(policy.get('advance_on', 'clear') or 'clear').lower()
        advance_on_cooldown = (advance_on == 'cooldown')
        proximity_initial_only = bool(policy.get('proximity_initial_only'))
        return looping, max_active, advance_on_cooldown, proximity_initial_only

    def _prune_tracking_sets(self, st, ents_set):
        """Limpia entidades inexistentes de los trackers del spawner (oleada/activos)."""
        # Always prune dead/missing entities from the current wave tracking
        if getattr(st, 'current_wave_entities', None) is not None:
            alive = set()
            for ent_id in list(st.current_wave_entities):
                if ent_id in ents_set:
                    alive.add(ent_id)
            st.current_wave_entities = alive

        # Prune active_entities as well (used for max_active enforcement)
        if getattr(st, 'active_entities', None) is not None:
            active_alive = set()
            for ent_id in list(st.active_entities):
                if ent_id in ents_set:
                    active_alive.add(ent_id)
            st.active_entities = active_alive

    def update(self, world, camera=None):
        # Advance frame index and reset per-frame caches
        self._frame_idx += 1
        comps = world.components
        # Gather blocked tiles with TTL cache
        solid, building = self._collect_blocked_tiles(world)
        # Global reserved tiles for this tick to avoid cross-spawner overlaps
        reserved_global = set()
        # Map walkability helper
        map_manager = getattr(world, 'map_manager', None)
        # Compute entities set once for all spawners this frame
        try:
            ents_set = set(world.entities)
        except Exception:
            ents_set = set()

        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]
            # Sync per-state visual once per frame (uses last known fsm_state)
            self._sync_spawner_visual(world, eid, cfg, st)
            # Política normalizada (looping, capacidad, forma de avanzar, proximidad híbrida)
            looping, max_active, advance_on_cooldown, proximity_initial_only = self._get_policy_flags(cfg)

            # Handle finished: either stop or auto-restart if looping is enabled
            if getattr(st, 'finished', False):
                if looping:
                    # Count down restart delay, then reset
                    if getattr(st, 'restart_cooldown_remaining', 0) > 0:
                        # Waiting for restart
                        try:
                            st.fsm_state = 'wait_restart'
                        except Exception:
                            pass
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
                    # In mixed proximity mode, require proximity again for initial start
                    if proximity_initial_only or getattr(cfg, 'between_waves_cooldown_frames', 0) > 0:
                        st.started = False
                        try:
                            st.initial_proximity_done = False
                        except Exception:
                            pass
                else:
                    try:
                        st.fsm_state = 'finished'
                    except Exception:
                        pass
                    continue

            # Only run if started and there is at least one wave
            if not st.started or not cfg.waves:
                # Awaiting trigger or missing waves data
                try:
                    st.fsm_state = 'await_trigger'
                except Exception:
                    pass
                continue

            # If we already spawned this wave and we advance on clear, wait until all are eliminated
            if st.spawned_this_wave and not advance_on_cooldown:
                # Only prune when we need to evaluate completion of the wave
                self._prune_tracking_sets(st, ents_set)
                if st.expected_this_wave > 0 and len(st.current_wave_entities) == 0:
                    # Wave completed
                    wave_num = st.current_wave_idx + 1
                    total_waves = len(cfg.waves)
                    if getattr(config, 'DEBUG_SPAWNER', False):
                        logger.info(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed")
                    else:
                        logger.debug(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed")
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
                            try:
                                st.fsm_state = 'wait_restart'
                            except Exception:
                                pass
                            if getattr(config, 'DEBUG_SPAWNER', False):
                                logger.info(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                            else:
                                logger.debug(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                            continue
                        else:
                            st.finished = True
                            try:
                                st.fsm_state = 'finished'
                            except Exception:
                                pass
                            if getattr(config, 'DEBUG_SPAWNER', False):
                                logger.info(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                            else:
                                logger.debug(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                            continue
                    else:
                        # Delay before next wave: prefer between-waves fixed cooldown when configured
                        bw = int(getattr(cfg, 'between_waves_cooldown_frames', 0) or 0)
                        base_cd = int(getattr(cfg, 'cooldown_frames', 0) or 0)
                        next_cd = bw if bw > 0 else base_cd
                        st.cooldown_remaining = max(st.cooldown_remaining, next_cd)
                else:
                    # Still waiting for monsters to be eliminated or none actually spawned yet
                    try:
                        st.fsm_state = 'wait_clear'
                    except Exception:
                        pass
                    continue

            # In cooldown-advance mode: if we already spawned all waves, wait until all active entities are cleared to finish/restart
            if advance_on_cooldown and st.current_wave_idx >= len(cfg.waves):
                # Prune only when we need to check active_entities
                self._prune_tracking_sets(st, ents_set)
                active_count = 0
                try:
                    active_count = len(getattr(st, 'active_entities', set()) or [])
                except Exception:
                    active_count = 0
                if active_count == 0:
                    if looping:
                        st.restart_cooldown_remaining = getattr(cfg, 'restart_cooldown_frames', getattr(cfg, 'cooldown_frames', 0))
                        st.finished = True
                        try:
                            st.fsm_state = 'wait_restart'
                        except Exception:
                            pass
                        if getattr(config, 'DEBUG_SPAWNER', False):
                            logger.info(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                        else:
                            logger.debug(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                    else:
                        st.finished = True
                        try:
                            st.fsm_state = 'finished'
                        except Exception:
                            pass
                        if getattr(config, 'DEBUG_SPAWNER', False):
                            logger.info(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                        else:
                            logger.debug(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                # Either way, do nothing else this tick while waiting
                if active_count > 0:
                    try:
                        st.fsm_state = 'wait_clear'
                    except Exception:
                        pass
                continue

            # Cooldown handling (only matters when spawning a new wave)
            if st.cooldown_remaining > 0:
                try:
                    st.fsm_state = 'wait_cooldown'
                except Exception:
                    pass
                st.cooldown_remaining -= 1
                continue

            # Determine current wave to spawn
            # About to spawn a wave
            try:
                st.fsm_state = 'spawning_wave'
            except Exception:
                pass
            wave = cfg.waves[min(st.current_wave_idx, len(cfg.waves) - 1)]
            spawns = wave.get('spawns', [])
            if not spawns:
                # Nothing to spawn -> consider wave instantly completed and advance
                wave_num = st.current_wave_idx + 1
                total_waves = len(cfg.waves)
                if getattr(config, 'DEBUG_SPAWNER', False):
                    logger.info(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed (empty)")
                else:
                    logger.debug(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed (empty)")
                st.current_wave_idx += 1
                if st.current_wave_idx >= len(cfg.waves):
                    if looping:
                        st.spawned_this_wave = False
                        st.expected_this_wave = 0
                        st.restart_cooldown_remaining = getattr(cfg, 'restart_cooldown_frames', getattr(cfg, 'cooldown_frames', 0))
                        st.finished = True
                        if getattr(config, 'DEBUG_SPAWNER', False):
                            logger.info(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                        else:
                            logger.debug(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                    else:
                        st.finished = True
                        if getattr(config, 'DEBUG_SPAWNER', False):
                            logger.info(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                        else:
                            logger.debug(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                else:
                    bw = int(getattr(cfg, 'between_waves_cooldown_frames', 0) or 0)
                    st.cooldown_remaining = (bw if bw > 0 else getattr(cfg, 'cooldown_frames', 0))
                continue

            # Issue spawn requests for this wave (avoid overlaps via spiral search)
            # Reset tracking for this wave
            st.current_wave_entities.clear()
            total_expected = 0
            attempted_total = 0
            # Tiles currently occupied by actors (compute once per frame)
            npc_tiles = self._collect_npc_tiles(world)
            # Reserve tiles chosen in this wave to prevent duplicates
            reserved_tiles = set()

            # Enforce max_active across waves if configured
            capacity_left = None
            if max_active > 0 and getattr(st, 'active_entities', None) is not None:
                # Prune only when we need to evaluate active_entities for capacity
                self._prune_tracking_sets(st, ents_set)
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
                # Determine placement shape/radius from config
                sr = getattr(cfg, 'spawn_radius', None)
                shape = str(getattr(cfg, 'spawner_shape', 'circle') or 'circle').lower()
                # Apply capacity limit if present
                if capacity_left is not None:
                    if capacity_left <= 0:
                        continue
                    count = max(0, min(count, capacity_left))
                attempted_total += count
                for _ in range(count):
                    ax, ay = cfg.anchor_tile
                    chosen = choose_spawn_tile(
                        ax,
                        ay,
                        solid,
                        building,
                        npc_tiles,
                        reserved_tiles,
                        reserved_global,
                        map_manager,
                        min_px_dist,
                        fallback_max,
                        sr,
                        shape,
                    )
                    if chosen is None:
                        continue
                    reserved_tiles.add(chosen)
                    reserved_global.add(chosen)
                    req_eid = world.create_entity()
                    # Build optional defend area metadata
                    defend_center, defend_radius_px, defend_leash, defend_shape = self._compute_defend_metadata(
                        cfg, sr, fallback_max, shape
                    )

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
                msg = f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} placed {total_expected}/{attempted_total} spawns"
                if getattr(config, 'DEBUG_SPAWNER', False):
                    logger.info(msg)
                else:
                    logger.debug(msg)
            except Exception:
                pass

            # If nothing could be placed, we still consider the wave done immediately
            st.expected_this_wave = total_expected
            st.spawned_this_wave = True
            if total_expected == 0:
                # No entities actually spawned -> mark as completed immediately
                wave_num = st.current_wave_idx + 1
                total_waves = len(cfg.waves)
                if getattr(config, 'DEBUG_SPAWNER', False):
                    logger.info(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed (no spots)")
                else:
                    logger.debug(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed (no spots)")
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
                        if getattr(config, 'DEBUG_SPAWNER', False):
                            logger.info(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                        else:
                            logger.debug(f"[Spawner] {cfg.template_id}:{eid} cycle completed; scheduling restart in {st.restart_cooldown_remaining} frames")
                    else:
                        st.finished = True
                        if getattr(config, 'DEBUG_SPAWNER', False):
                            logger.info(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                        else:
                            logger.debug(f"[Spawner] {cfg.template_id}:{eid} all waves completed")
                else:
                    st.cooldown_remaining = cfg.cooldown_frames
            else:
                # After issuing requests
                bw = int(getattr(cfg, 'between_waves_cooldown_frames', 0) or 0)
                st.cooldown_remaining = (bw if bw > 0 else getattr(cfg, 'cooldown_frames', 0))
                if advance_on_cooldown:
                    # Immediately move to next wave; completion will be checked after all waves are spawned
                    st.current_wave_idx += 1
                    st.spawned_this_wave = False
                    # expected_this_wave is not used for advancing in this mode
