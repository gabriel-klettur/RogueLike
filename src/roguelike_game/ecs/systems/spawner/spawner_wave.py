from __future__ import annotations

import logging
from typing import Any, Iterable, Set, Tuple

import roguelike_engine.config.config as config
from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest
from .placement_utils import choose_spawn_tile
from .spawner_utils import get_policy_flags, prune_tracking_sets, compute_defend_metadata

logger = logging.getLogger(__name__)

Tile = Tuple[int, int]


def process_spawner(
    world: Any,
    eid: int,
    cfg: Any,
    st: Any,
    solid: Set[Tile],
    building: Set[Tile],
    caches,
    ents_set: Iterable[int],
    reserved_global: Set[Tile],
) -> None:
    """Advance one spawner through its FSM and issue SpawnRequest entities when needed.

    Mirrors original behavior while being easier to read and test.
    """
    looping, max_active, advance_on_cooldown, proximity_initial_only = get_policy_flags(cfg)

    # Finished handling (restart if looping)
    if getattr(st, 'finished', False):
        if looping:
            if getattr(st, 'restart_cooldown_remaining', 0) > 0:
                try:
                    st.fsm_state = 'wait_restart'
                except Exception:
                    pass
                st.restart_cooldown_remaining -= 1
                return
            st.finished = False
            st.current_wave_idx = 0
            st.spawned_this_wave = False
            st.expected_this_wave = 0
            try:
                st.current_wave_entities.clear()
            except Exception:
                st.current_wave_entities = set()
            st.cooldown_remaining = 0
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
            return

    # Await trigger / no waves
    if not st.started or not cfg.waves:
        try:
            st.fsm_state = 'await_trigger'
        except Exception:
            pass
        return

    # Advance on clear: wait for elimination of current wave
    if st.spawned_this_wave and not advance_on_cooldown:
        prune_tracking_sets(st, ents_set)
        if st.expected_this_wave > 0 and len(st.current_wave_entities) == 0:
            wave_num = st.current_wave_idx + 1
            total_waves = len(cfg.waves)
            if getattr(config, 'DEBUG_SPAWNER', False):
                logger.info(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed")
            else:
                logger.debug(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed")
            st.current_wave_idx += 1
            st.spawned_this_wave = False
            st.expected_this_wave = 0
            if st.current_wave_idx >= len(cfg.waves):
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
                    return
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
                    return
            else:
                bw = int(getattr(cfg, 'between_waves_cooldown_frames', 0) or 0)
                base_cd = int(getattr(cfg, 'cooldown_frames', 0) or 0)
                st.cooldown_remaining = max(st.cooldown_remaining, bw if bw > 0 else base_cd)
        else:
            try:
                st.fsm_state = 'wait_clear'
            except Exception:
                pass
            return

    # Advance on cooldown: after all waves spawned, wait for active clear
    if advance_on_cooldown and st.current_wave_idx >= len(cfg.waves):
        prune_tracking_sets(st, ents_set)
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
        else:
            try:
                st.fsm_state = 'wait_clear'
            except Exception:
                pass
        return

    # Cooldown gate
    if st.cooldown_remaining > 0:
        try:
            st.fsm_state = 'wait_cooldown'
        except Exception:
            pass
        st.cooldown_remaining -= 1
        return

    # Begin spawning this wave
    try:
        st.fsm_state = 'spawning_wave'
    except Exception:
        pass

    wave = cfg.waves[min(st.current_wave_idx, len(cfg.waves) - 1)]
    spawns = wave.get('spawns', [])
    # Reset tracking for this wave as in the legacy system
    try:
        st.current_wave_entities.clear()
    except Exception:
        st.current_wave_entities = set()
    if not spawns:
        wave_num = st.current_wave_idx + 1
        total_waves = len(cfg.waves)
        if getattr(config, 'DEBUG_SPAWNER', False):
            logger.info(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed (empty)")
        else:
            logger.debug(f"[Spawner] {cfg.template_id}:{eid} wave {wave_num}/{total_waves} completed (empty)")
        st.current_wave_idx += 1
        if st.current_wave_idx >= len(cfg.waves):
            if looping:
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
        return

    comps = world.components
    total_expected = 0
    attempted_total = 0
    npc_tiles = caches.collect_npc_tiles(world)
    reserved_tiles: Set[Tile] = set()

    capacity_left = None
    if max_active > 0 and getattr(st, 'active_entities', None) is not None:
        prune_tracking_sets(st, ents_set)
        try:
            capacity_left = max(0, max_active - len(st.active_entities))
        except Exception:
            capacity_left = max_active
    if capacity_left is not None and capacity_left <= 0:
        st.cooldown_remaining = max(st.cooldown_remaining, getattr(cfg, 'cooldown_frames', 0))
        return

    for entry in spawns:
        if entry.get('kind') != 'monster':
            continue
        proto = entry.get('id', 'barbol')
        count = int(entry.get('count', 1))
        spread = int(entry.get('spread_radius', 3))
        fallback_max = int(entry.get('spread_fallback_max', max(spread, 8)))
        min_px_dist = int(entry.get('min_px_distance', 0))
        sr = getattr(cfg, 'spawn_radius', None)
        shape = str(getattr(cfg, 'spawner_shape', 'circle') or 'circle').lower()
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
                getattr(world, 'map_manager', None),
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
            defend_center, defend_radius_px, defend_leash, defend_shape = compute_defend_metadata(
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

    st.expected_this_wave = total_expected
    st.spawned_this_wave = True

    if total_expected == 0:
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
            policy = getattr(cfg, 'policy', {}) or {}
            looping = bool(policy.get('loop') or policy.get('repeat') or policy.get('restart_on_done'))
            if looping:
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
            st.cooldown_remaining = getattr(cfg, 'cooldown_frames', 0)
    else:
        bw = int(getattr(cfg, 'between_waves_cooldown_frames', 0) or 0)
        st.cooldown_remaining = (bw if bw > 0 else getattr(cfg, 'cooldown_frames', 0))
        if advance_on_cooldown:
            st.current_wave_idx += 1
            st.spawned_this_wave = False
            # expected_this_wave is not used for advancing in this mode
