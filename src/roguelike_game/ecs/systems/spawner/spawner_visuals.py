from __future__ import annotations

import logging
from typing import Any, Dict, Optional, Set, Tuple

from roguelike_engine.config.config_tiles import TILE_SIZE
import roguelike_engine.config.config as config
from roguelike_engine.config.map_config import global_map_settings

logger = logging.getLogger(__name__)


class SpawnerVisualSync:
    """Stateful helper that manages exclusive runtime visibility of spawner visuals.

    It tags Building objects with linkage to the spawner ("_spawner_eid") and toggles
    their "runtime_hidden" flag according to the spawner's current state.
    """

    def __init__(self) -> None:
        self._last_visual_id: Dict[int, Optional[int]] = {}
        self._visual_log_state: Dict[int, Tuple[Optional[int], Optional[str], int]] = {}
        self._dup_warned: Set[Tuple[int, int]] = set()
        self._visual_enabled_last: Dict[int, bool] = {}
        self._log_interval_frames: int = 30

    # ---------------------- Visual helpers ----------------------
    @staticmethod
    def current_state_key(st: Any) -> Optional[str]:
        try:
            tok = getattr(st, 'visual_override_token', None)
            if tok:
                return str(tok).strip().lower()
        except Exception:
            pass
        try:
            cur = getattr(st, 'fsm_state', None)
            return str(cur).strip().lower() if cur is not None else None
        except Exception:
            return None

    @staticmethod
    def desired_building_for_state(cfg: Any, st: Any) -> Optional[int]:
        desired = getattr(cfg, 'building_id', None)
        mapping = getattr(cfg, 'state_visuals', None)
        if not mapping:
            return desired
        norm: Dict[str, int] = {}
        try:
            for k, v in mapping.items():
                try:
                    norm[str(k).strip().lower()] = int(v) if v is not None else None  # type: ignore
                except Exception:
                    norm[str(k).strip().lower()] = v  # type: ignore
        except Exception:
            norm = {}
        key = SpawnerVisualSync.current_state_key(st)
        if key and key in norm:
            return norm[key]
        if key:
            try:
                camel = ''.join(part.title() for part in key.split('_'))
                if camel.lower() in norm:
                    return norm[camel.lower()]
                if camel in mapping:
                    val = mapping[camel]
                    try:
                        return int(val) if val is not None else None
                    except Exception:
                        return val  # type: ignore
            except Exception:
                pass
        return desired

    @staticmethod
    def _find_linked_building(world, eid: int):
        blds = getattr(world, 'buildings', []) or []
        for ob in blds:
            try:
                if getattr(ob, '_spawner_eid', None) == eid:
                    return ob
            except Exception:
                continue
        return None

    @staticmethod
    def _find_building_by_id(world, building_id: Optional[int]):
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

    def sync(self, world, eid: int, cfg: Any, st: Any, frame_idx: int) -> None:
        mapping_ids: Set[int] = set()
        try:
            vis_map = getattr(cfg, 'state_visuals', None) or {}
            if isinstance(vis_map, dict):
                for _k, _v in vis_map.items():
                    try:
                        if isinstance(_v, dict):
                            bid = int(_v.get('instance_id') or _v.get('id') or _v.get('building_instance_id'))
                        else:
                            bid = int(_v)
                        mapping_ids.add(int(bid))
                    except Exception:
                        continue
            if getattr(cfg, 'building_id', None) is not None:
                try:
                    mapping_ids.add(int(getattr(cfg, 'building_id')))
                except Exception:
                    pass
        except Exception:
            mapping_ids = set()

        try:
            editor_active = bool(getattr(getattr(world, 'state', None), 'spawner_editor_active', False))
        except Exception:
            editor_active = False

        if editor_active:
            try:
                for ob in getattr(world, 'buildings', []) or []:
                    try:
                        bid = getattr(ob, 'id', None)
                        if bid in mapping_ids:
                            setattr(ob, '_spawner_eid', eid)
                            setattr(ob, '_world_ref', world)
                            setattr(ob, '_is_spawner_visual', True)
                            setattr(ob, 'runtime_hidden', False)
                    except Exception:
                        continue
            except Exception:
                pass
            self._last_visual_id[eid] = None
            return

        if not getattr(cfg, 'visible_in_game', False):
            try:
                prev_enabled = self._visual_enabled_last.get(eid, True)
                if prev_enabled and getattr(config, 'DEBUG_SPAWNER', False):
                    logger.debug(f"[SpawnerRuntime] eid={eid} visuals disabled -> hiding any linked buildings")
                self._visual_enabled_last[eid] = False
            except Exception:
                pass
            try:
                for ob in getattr(world, 'buildings', []) or []:
                    try:
                        if getattr(ob, '_spawner_eid', None) == eid or (getattr(ob, 'id', None) in mapping_ids):
                            setattr(ob, 'runtime_hidden', True)
                            try:
                                setattr(ob, '_spawner_eid', eid)
                                setattr(ob, '_world_ref', world)
                                setattr(ob, '_is_spawner_visual', True)
                            except Exception:
                                pass
                    except Exception:
                        continue
            except Exception:
                pass
            self._last_visual_id[eid] = None
            return

        desired = SpawnerVisualSync.desired_building_for_state(cfg, st)
        prev = self._last_visual_id.get(eid)
        try:
            state_tok = getattr(st, 'fsm_state', None)
            last = self._visual_log_state.get(eid)
            if (last is None or last[0] != desired or last[1] != state_tok) and getattr(config, 'DEBUG_SPAWNER', False):
                logger.debug(f"[SpawnerRuntime] eid={eid} visual desired={desired} prev={prev} state={state_tok}")
                self._visual_log_state[eid] = (desired, state_tok, frame_idx)
            self._visual_enabled_last[eid] = True
        except Exception:
            pass

        try:
            dup_count = 0
            try:
                tx, ty = cfg.anchor_tile
                zone = getattr(cfg, 'zone', None) or 'lobby'
                off_x, off_y = global_map_settings.zone_offsets.get(str(zone), (0, 0))
                anchor_cx = int((int(tx) - int(off_x)) * TILE_SIZE + TILE_SIZE // 2)
                anchor_cy = int((int(ty) - int(off_y)) * TILE_SIZE + TILE_SIZE // 2)
            except Exception:
                anchor_cx = None
                anchor_cy = None
            cur_key = SpawnerVisualSync.current_state_key(st)
            off_dx, off_dy = 0, 0
            try:
                offs = getattr(cfg, 'visuals_offsets_px', None) or {}
                if cur_key and cur_key in offs:
                    off_dx, off_dy = offs[cur_key]
                elif cur_key:
                    camel = ''.join(part.title() for part in cur_key.split('_'))
                    if camel.lower() in offs:
                        off_dx, off_dy = offs[camel.lower()]
            except Exception:
                off_dx, off_dy = 0, 0

            eff_life = {}
            try:
                life_map = getattr(cfg, 'visuals_life', None) or {}
                base = getattr(cfg, 'life_defaults', None) or {}
                cur_key = SpawnerVisualSync.current_state_key(st)
                if isinstance(base, dict):
                    eff_life.update(base)
                if cur_key and isinstance(life_map, dict) and cur_key in life_map and isinstance(life_map[cur_key], dict):
                    eff_life.update(life_map[cur_key])
            except Exception:
                eff_life = {}

            for ob in getattr(world, 'buildings', []) or []:
                try:
                    bid = getattr(ob, 'id', None)
                    if bid in mapping_ids:
                        setattr(ob, '_spawner_eid', eid)
                        setattr(ob, '_world_ref', world)
                        setattr(ob, '_is_spawner_visual', True)
                        try:
                            setattr(ob, '_spawner_visual_life_cfg', eff_life if eff_life else None)
                        except Exception:
                            pass
                        visible_this = (bid == desired)
                        setattr(ob, 'runtime_hidden', not visible_this)
                        if visible_this and anchor_cx is not None and anchor_cy is not None:
                            try:
                                if getattr(ob, 'zone', None) != zone:
                                    setattr(ob, 'zone', zone)
                                if not bool(getattr(ob, '_spawner_visual_dragging', False)):
                                    setattr(ob, 'rel_x', int(anchor_cx + int(off_dx)))
                                    setattr(ob, 'rel_y', int(anchor_cy + int(off_dy)))
                            except Exception:
                                pass
                    if bid == desired:
                        dup_count += 1
                except Exception:
                    continue
            if desired is not None and dup_count > 1:
                key = (eid, int(desired))
                if key not in self._dup_warned:
                    logger.warning(f"[SpawnerRuntime] Duplicate Building objects in world with id={desired} for eid={eid}: count={dup_count}")
                    self._dup_warned.add(key)
        except Exception:
            pass

        if desired == prev:
            return

        cur = SpawnerVisualSync._find_linked_building(world, eid)
        if cur is not None and getattr(cur, 'id', None) != desired:
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
            target = cur if cur and getattr(cur, 'id', None) == desired else SpawnerVisualSync._find_building_by_id(world, int(desired))
            if target is not None:
                try:
                    setattr(target, '_spawner_eid', eid)
                    setattr(target, '_world_ref', world)
                    setattr(target, '_is_spawner_visual', True)
                    setattr(target, 'runtime_hidden', False)
                    try:
                        copies = [ob for ob in getattr(world, 'buildings', []) or [] if getattr(ob, 'id', None) == desired]
                        if len(copies) > 1:
                            key = (eid, int(desired))
                            if key not in self._dup_warned:
                                logger.warning(f"[SpawnerRuntime] Found {len(copies)} Building objects with id={desired} when linking to eid={eid}")
                                self._dup_warned.add(key)
                    except Exception:
                        pass
                    if getattr(config, 'DEBUG_SPAWNER', False):
                        logger.debug(f"[SpawnerRuntime] Linked spawner eid={eid} to building id={desired}")
                except Exception:
                    pass
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
