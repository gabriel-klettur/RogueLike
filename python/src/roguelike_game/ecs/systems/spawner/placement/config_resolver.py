from __future__ import annotations

import json
import ast
import logging
from typing import Any, Dict, List, Optional, Tuple

from roguelike_engine.config import config
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.components.spawner.spawner_config import SpawnerConfig

logger = logging.getLogger(__name__)


def resolve_config(tpl: Dict[str, Any], inst: Dict[str, Any], waves_by_id: Dict[str, List[Dict[str, Any]]]) -> SpawnerConfig:
    trigger = dict(tpl.get("trigger", {}))
    policy = dict(tpl.get("policy", {}))
    spawn_radius = tpl.get("spawn_radius")
    spawner_shape = str(tpl.get("spawner_shape", "circle")).lower()
    defend_spawn = bool(tpl.get("defend_spawn", False))
    defend_leash = bool(tpl.get("defend_leash", True))
    visible_in_game = bool(tpl.get("visible_in_game", False))
    building_id = tpl.get("building_id")
    state_visuals: Dict[str, Any] = {}
    waves_id = tpl.get("waves_id")
    raw_waves = tpl.get("waves", [])
    waves: List[Dict[str, Any]] = []
    if isinstance(waves_id, str) and waves_id in waves_by_id:
        waves = waves_by_id[waves_id]
    else:
        try:
            if isinstance(raw_waves, str):
                s = raw_waves.strip()
                parsed = None
                try:
                    parsed = json.loads(s)
                except Exception:
                    try:
                        parsed = ast.literal_eval(s)
                    except Exception:
                        parsed = None
                if isinstance(parsed, list):
                    waves = parsed
            elif isinstance(raw_waves, list):
                waves = raw_waves
        except Exception:
            waves = []
    waves = [w for w in waves if isinstance(w, dict)]
    spawner_type = tpl.get("spawner_type", "invisible")

    life_defaults: Optional[Dict[str, Any]] = None
    hp_scope: str = str(inst.get('overrides', {}).get('hp_scope', 'per_state')).strip().lower() if isinstance(inst.get('overrides'), dict) else 'per_state'
    try:
        if isinstance(inst.get('overrides'), dict):
            ld = inst['overrides'].get('life_defaults')
            if isinstance(ld, dict):
                life_defaults = {}
                if 'damageable' in ld:
                    life_defaults['damageable'] = bool(ld.get('damageable'))
                if 'max_hp' in ld and ld.get('max_hp') is not None:
                    try:
                        life_defaults['max_hp'] = int(ld.get('max_hp'))
                    except Exception:
                        pass
                if 'flash_on_hit' in ld:
                    life_defaults['flash_on_hit'] = bool(ld.get('flash_on_hit'))
                if 'flash_color' in ld and isinstance(ld.get('flash_color'), (list, tuple)) and len(ld.get('flash_color')) >= 3:
                    try:
                        r, g, b = int(ld['flash_color'][0]), int(ld['flash_color'][1]), int(ld['flash_color'][2])
                        life_defaults['flash_color'] = [max(0, min(r, 255)), max(0, min(g, 255)), max(0, min(b, 255))]
                    except Exception:
                        pass
                if 'flash_duration_s' in ld and ld.get('flash_duration_s') is not None:
                    try:
                        life_defaults['flash_duration_s'] = float(ld.get('flash_duration_s'))
                    except Exception:
                        pass
                if 'hp_reset_on_enter' in ld and isinstance(ld.get('hp_reset_on_enter'), str):
                    life_defaults['hp_reset_on_enter'] = str(ld.get('hp_reset_on_enter')).strip().lower()
                if 'sources' in ld:
                    try:
                        srcs = ld.get('sources')
                        norm: list[str] | None = None
                        if isinstance(srcs, str):
                            norm = [str(srcs).strip().lower()]
                        elif isinstance(srcs, (list, tuple)):
                            tmp = []
                            for s in srcs:
                                try:
                                    tmp.append(str(s).strip().lower())
                                except Exception:
                                    continue
                            norm = tmp
                        if norm is not None:
                            life_defaults['sources'] = norm
                    except Exception:
                        pass
    except Exception:
        life_defaults = life_defaults or None

    try:
        ivis = inst.get("visuals")
        if isinstance(ivis, dict):
            for sk, sv in ivis.items():
                try:
                    if isinstance(sv, dict):
                        bid = None
                        try:
                            bid = int(sv.get('instance_id') or sv.get('id') or sv.get('building_instance_id'))
                        except Exception:
                            bid = None
                        if bid is not None:
                            state_visuals[str(sk)] = bid
                        else:
                            state_visuals[str(sk)] = sv  # type: ignore
                    else:
                        state_visuals[str(sk)] = int(sv) if sv is not None else sv
                except Exception:
                    try:
                        state_visuals[str(sk)] = sv  # type: ignore
                    except Exception:
                        pass
        try:
            if getattr(config, 'DEBUG_SPAWNER', False):
                logger.debug(f"[SpawnerPlacementSystem] _resolve_config: inst_id={inst.get('id')} visuals_len={len((ivis or {})) if isinstance(ivis, dict) else 'N/A'} visible_in_game={inst.get('overrides',{}).get('visible_in_game')}")
        except Exception:
            pass
    except Exception:
        pass

    for key, value in inst.get("overrides", {}).items():
        if key.startswith("trigger."):
            trigger[key.split(".", 1)[1]] = value
        elif key.startswith("policy."):
            policy[key.split(".", 1)[1]] = value
        elif key == "spawner_type":
            spawner_type = value
        elif key == "spawn_radius":
            spawn_radius = value
        elif key == "spawner_shape":
            spawner_shape = str(value).lower()
        elif key == "defend_spawn":
            defend_spawn = bool(value)
        elif key == "defend_leash":
            defend_leash = bool(value)
        elif key == "visible_in_game":
            visible_in_game = bool(value)
        elif key == "building_id":
            try:
                building_id = int(value) if value is not None else None
            except Exception:
                building_id = value
        elif key.startswith("visuals."):
            try:
                st_id = key.split(".", 1)[1]
            except Exception:
                st_id = None
            if st_id:
                try:
                    state_visuals[str(st_id)] = int(value) if value is not None else value  # type: ignore
                except Exception:
                    try:
                        state_visuals[str(st_id)] = value  # type: ignore
                    except Exception:
                        pass

    try:
        if inst.get("building_id") is not None:
            building_id = int(inst.get("building_id"))
    except Exception:
        building_id = inst.get("building_id", building_id)

    try:
        tv = tpl.get("visuals")
        if isinstance(tv, dict):
            for sk, sv in tv.items():
                if str(sk) not in state_visuals:
                    try:
                        state_visuals[str(sk)] = int(sv) if sv is not None else sv
                    except Exception:
                        try:
                            state_visuals[str(sk)] = sv  # type: ignore
                        except Exception:
                            pass
    except Exception:
        pass

    fps = getattr(config, "FPS", 60)
    cooldown_s = float(policy.get("cooldown_s", 10.0))
    cooldown_frames = int(round(cooldown_s * fps))
    restart_cooldown_s = float(policy.get("restart_cooldown_s", cooldown_s))
    restart_cooldown_frames = int(round(restart_cooldown_s * fps))
    between_waves_cooldown_s = float(policy.get("between_waves_cooldown_s", 0.0) or 0.0)
    between_waves_cooldown_frames = int(round(between_waves_cooldown_s * fps))

    zone = inst.get("zone", "lobby")
    local_tx, local_ty = tuple(inst.get("tile", (0, 0)))
    off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
    anchor_tile = (off_x + int(local_tx), off_y + int(local_ty))

    try:
        if not visible_in_game and state_visuals:
            visible_in_game = True
    except Exception:
        pass

    visuals_offsets_px: Dict[str, Tuple[int, int]] = {}
    try:
        ivis = inst.get("visuals")
        if isinstance(ivis, dict):
            for sk, sv in ivis.items():
                try:
                    if isinstance(sv, dict):
                        off = sv.get('offset')
                        if isinstance(off, (list, tuple)) and len(off) == 2:
                            dx, dy = int(off[0]), int(off[1])
                            visuals_offsets_px[str(sk).strip().lower()] = (dx, dy)
                except Exception:
                    pass
    except Exception:
        pass

    visuals_life: Dict[str, Dict[str, Any]] = {}
    try:
        ivis2 = inst.get('visuals')
        if isinstance(ivis2, dict):
            for sk, sv in ivis2.items():
                try:
                    life_block = sv.get('life') if isinstance(sv, dict) else None
                    if isinstance(life_block, dict):
                        key_norm = str(sk).strip().lower()
                        eff: Dict[str, Any] = {}
                        if 'damageable' in life_block:
                            eff['damageable'] = bool(life_block.get('damageable'))
                        if life_block.get('max_hp') is not None:
                            try:
                                eff['max_hp'] = int(life_block.get('max_hp'))
                            except Exception:
                                pass
                        if 'flash_on_hit' in life_block:
                            eff['flash_on_hit'] = bool(life_block.get('flash_on_hit'))
                        if isinstance(life_block.get('flash_color'), (list, tuple)) and len(life_block.get('flash_color')) >= 3:
                            try:
                                r, g, b = int(life_block['flash_color'][0]), int(life_block['flash_color'][1]), int(life_block['flash_color'][2])
                                eff['flash_color'] = [max(0, min(r, 255)), max(0, min(g, 255)), max(0, min(b, 255))]
                            except Exception:
                                pass
                        if life_block.get('flash_duration_s') is not None:
                            try:
                                eff['flash_duration_s'] = float(life_block.get('flash_duration_s'))
                            except Exception:
                                pass
                        if isinstance(life_block.get('hp_reset_on_enter'), str):
                            eff['hp_reset_on_enter'] = str(life_block.get('hp_reset_on_enter')).strip().lower()
                        if isinstance(life_block.get('next_step_by_hp'), str):
                            eff['next_step_by_hp'] = str(life_block.get('next_step_by_hp'))
                        if life_block.get('end_logic') is not None:
                            eff['end_logic'] = bool(life_block.get('end_logic'))
                        try:
                            srcs2 = life_block.get('sources')
                            if isinstance(srcs2, str):
                                eff['sources'] = [str(srcs2).strip().lower()]
                            elif isinstance(srcs2, (list, tuple)):
                                tmp2 = []
                                for s in srcs2:
                                    try:
                                        tmp2.append(str(s).strip().lower())
                                    except Exception:
                                        continue
                                eff['sources'] = tmp2
                        except Exception:
                            pass
                        if eff:
                            visuals_life[key_norm] = eff
                except Exception:
                    continue
    except Exception:
        visuals_life = visuals_life or {}

    visuals_fx: Dict[str, Dict[str, Any]] = {}
    try:
        ivis3 = inst.get('visuals')
        if isinstance(ivis3, dict):
            for sk, sv in ivis3.items():
                try:
                    fx_block = sv.get('fx') if isinstance(sv, dict) else None
                    if isinstance(fx_block, dict):
                        key_norm_fx = str(sk).strip().lower()
                        visuals_fx[key_norm_fx] = dict(fx_block)
                except Exception:
                    continue
    except Exception:
        visuals_fx = visuals_fx or {}

    return SpawnerConfig(
        template_id=tpl.get("id", ""),
        zone=inst.get("zone", tpl.get("zone", "lobby")),
        anchor_tile=anchor_tile,
        spawner_type=spawner_type,
        trigger=trigger,
        policy=policy,
        waves=waves,
        cooldown_frames=cooldown_frames,
        restart_cooldown_frames=restart_cooldown_frames,
        between_waves_cooldown_frames=between_waves_cooldown_frames,
        spawn_radius=spawn_radius,
        spawner_shape=spawner_shape,
        defend_spawn=defend_spawn,
        defend_leash=defend_leash,
        visible_in_game=visible_in_game,
        building_id=building_id,
        state_visuals=state_visuals or None,
        visuals_offsets_px=visuals_offsets_px or None,
        life_defaults=life_defaults or None,
        hp_scope=hp_scope or 'per_state',
        visuals_life=visuals_life or None,
        visuals_fx=visuals_fx or None,
    )
