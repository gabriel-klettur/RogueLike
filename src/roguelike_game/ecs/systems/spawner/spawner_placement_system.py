"""
SpawnerPlacementSystem: loads spawner templates and instances from JSON and creates ECS entities
with SpawnerConfig + SpawnerState components.
"""
from __future__ import annotations

import json
import ast
import os
from typing import Any, Dict, List, Optional

from roguelike_engine.config import config
from roguelike_game.ecs.components.spawner.spawner_config import SpawnerConfig
from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState
from roguelike_engine.config.map_config import global_map_settings
import logging
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config_z_layer import Z_LAYERS, DEFAULT_Z
from roguelike_engine.buildings.building import Building

# Optional: FSM Editor bridge (for validation of set ids). Keep non-fatal.
try:
    from roguelike_editors.fsm.services.fsm_runtime_bridge import get_set as _fsm_get_set
except Exception:  # pragma: no cover - editor may not be available in some contexts
    _fsm_get_set = None

logger = logging.getLogger(__name__)


class SpawnerPlacementSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._loaded = False
        self._templates: Dict[str, Dict[str, Any]] = {}
        self._waves: Dict[str, List[Dict[str, Any]]] = {}

    # Phase 2: compile a visual FSM set id for the FSM Editor based on the resolved config.
    # This is purely metadata for tools/UI and does not change runtime behavior.
    def _compile_fsm_set(self, cfg: SpawnerConfig) -> (str, Dict[str, Any]):
        try:
            pol = dict(getattr(cfg, 'policy', {}) or {})
            trig = str(((getattr(cfg, 'trigger', {}) or {}).get('type') or 'proximity')).lower()
            advance_on = str(pol.get('advance_on', 'cooldown') or 'cooldown').lower()
            bwc_frames = int(getattr(cfg, 'between_waves_cooldown_frames', 0) or 0)
            bwc = bwc_frames > 0
            prox_init_only = bool(pol.get('proximity_initial_only', False))
            loop = bool(pol.get('restart_on_done') or pol.get('loop') or pol.get('repeat'))
            max_active = int(pol.get('max_active', 0) or 0)
            mode = pol.get('mode', '')
            # Select set id
            if advance_on == 'clear':
                set_id = 'Spawner_Waves_Clear'
            elif bwc:
                set_id = 'Spawner_Periodic_BetweenWaves'
            else:
                set_id = 'Spawner_Periodic_Cooldown'
            params: Dict[str, Any] = {
                'trigger': trig,
                'advance_on': advance_on,
                'between_waves_cooldown_frames': bwc_frames,
                'proximity_initial_only': prox_init_only,
                'loop': loop,
                'cooldown_frames': int(getattr(cfg, 'cooldown_frames', 0) or 0),
                'restart_cooldown_frames': int(getattr(cfg, 'restart_cooldown_frames', 0) or 0),
                'max_active': max_active,
                'mode': mode,
                'spawner_shape': getattr(cfg, 'spawner_shape', 'circle'),
                'spawn_radius': getattr(cfg, 'spawn_radius', None),
                'template_id': getattr(cfg, 'template_id', ''),
            }
            return set_id, params
        except Exception:
            # Fallback to a sensible default
            return 'Spawner_Periodic_Cooldown', {
                'error': 'compile_failed'
            }

    # Phase 3: overrides from template/instance
    def _fsm_override_from(self, tpl: Dict[str, Any], inst: Dict[str, Any]) -> (Optional[str], Dict[str, Any]):
        """Read optional FSM override. Returns (set_id_or_None, params_dict). Supports:
        - Template-level block: { "fsm": { "set_id": str, "params": {..} } }
        - Instance-level block: { "fsm": { "set_id": str, "params": {..} } }
        - Instance overrides dot-notation: overrides { "fsm.set_id": str, "fsm.params.X": any }
        Instance has priority over template.
        """
        set_id: Optional[str] = None
        params: Dict[str, Any] = {}
        try:
            # Template block
            tfsm = tpl.get('fsm') if isinstance(tpl, dict) else None
            if isinstance(tfsm, dict):
                if isinstance(tfsm.get('params'), dict):
                    params.update(tfsm['params'])
                if isinstance(tfsm.get('set_id'), str):
                    set_id = tfsm['set_id']
            # Instance block
            if isinstance(inst, dict) and isinstance(inst.get('fsm'), dict):
                if isinstance(inst['fsm'].get('params'), dict):
                    params.update(inst['fsm']['params'])
                if isinstance(inst['fsm'].get('set_id'), str):
                    set_id = inst['fsm']['set_id'] or set_id
            # Dot-notation overrides
            ov = inst.get('overrides', {}) if isinstance(inst, dict) else {}
            if isinstance(ov, dict):
                for k, v in ov.items():
                    if k == 'fsm.set_id' and isinstance(v, str):
                        set_id = v
                    elif k.startswith('fsm.params.'):
                        key = k.split('.', 2)[2] if '.' in k else None
                        if key:
                            params[key] = v
        except Exception:
            pass
        return set_id, params

    def _load_templates(self) -> Dict[str, Dict[str, Any]]:
        base = config.DATA_DIR
        path = os.path.join(base, "spawners", "spawners_templates.json")
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            # normalize list into dict by id
            if isinstance(data, list):
                return {t["id"]: t for t in data}
            return data or {}
        except FileNotFoundError:
            return {}

    def _load_waves(self) -> Dict[str, List[Dict[str, Any]]]:
        """Load wave sets by ID from spawners_waves.json. Supports either
        { id: [ ...waves... ] } or { id: { "waves": [ ... ] } } formats.
        """
        base = config.DATA_DIR
        path = os.path.join(base, "spawners", "spawners_waves.json")
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
        except FileNotFoundError:
            return {}
        except json.JSONDecodeError:
            logger.warning("[SpawnerPlacementSystem] spawners_waves.json invalid JSON; ignoring")
            return {}
        if not isinstance(data, dict):
            return {}
        waves_map: Dict[str, List[Dict[str, Any]]] = {}
        for key, val in data.items():
            if isinstance(val, list):
                waves_map[key] = [w for w in val if isinstance(w, dict)]
            elif isinstance(val, dict) and isinstance(val.get("waves"), list):
                waves_map[key] = [w for w in val.get("waves", []) if isinstance(w, dict)]
        return waves_map

    def _load_instances(self) -> List[Dict[str, Any]]:
        base = config.DATA_DIR
        path = os.path.join(base, "spawners", "spawners_instances.json")
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            out = data if isinstance(data, list) else []
            try:
                if getattr(config, 'DEBUG_SPAWNER', False):
                    num = len(out)
                    with_vis = sum(1 for e in out if isinstance(e.get('visuals'), dict) and len(e.get('visuals') or {}) > 0)
                    logger.debug(f"[SpawnerPlacementSystem] _load_instances: read {num} entries (visuals>0 in {with_vis}) from {path}")
            except Exception:
                pass
            return out
        except FileNotFoundError:
            return []

    def _resolve_config(self, tpl: Dict[str, Any], inst: Dict[str, Any]) -> SpawnerConfig:
        # template base
        trigger = dict(tpl.get("trigger", {}))
        policy = dict(tpl.get("policy", {}))
        spawn_radius = tpl.get("spawn_radius")
        spawner_shape = str(tpl.get("spawner_shape", "circle")).lower()
        defend_spawn = bool(tpl.get("defend_spawn", False))
        defend_leash = bool(tpl.get("defend_leash", True))
        visible_in_game = bool(tpl.get("visible_in_game", False))
        building_id = tpl.get("building_id")
        # Optional visuals mapping by FSM state id -> building_id
        state_visuals: Dict[str, int] = {}
        # waves can be external by id, inline list, or a bad string to parse
        waves_id = tpl.get("waves_id")
        raw_waves = tpl.get("waves", [])
        waves: List[Dict[str, Any]] = []
        # Prefer referenced waves by id
        if isinstance(waves_id, str) and waves_id in self._waves:
            waves = self._waves[waves_id]
        else:
            # Backward compatibility with inline waves
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
        # keep only dict entries
        waves = [w for w in waves if isinstance(w, dict)]
        spawner_type = tpl.get("spawner_type", "invisible")

        # apply overrides in dot-notation
        # Instance-level visuals block (full dict)
        try:
            ivis = inst.get("visuals")
            if isinstance(ivis, dict):
                for sk, sv in ivis.items():
                    try:
                        # New format: dict {instance_id, template_id}
                        if isinstance(sv, dict):
                            bid = None
                            try:
                                bid = int(sv.get('instance_id') or sv.get('id') or sv.get('building_instance_id'))
                            except Exception:
                                bid = None
                            if bid is not None:
                                state_visuals[str(sk)] = bid
                            else:
                                # Fallback keep as-is
                                state_visuals[str(sk)] = sv  # type: ignore
                        else:
                            state_visuals[str(sk)] = int(sv) if sv is not None else sv
                    except Exception:
                        # keep as-is if not int-castable
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
                # Dot-notation: visuals.StateId -> building_id
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

        # Allow instance root-level building_id to override as well
        try:
            if inst.get("building_id") is not None:
                building_id = int(inst.get("building_id"))
        except Exception:
            building_id = inst.get("building_id", building_id)

        # Template-level visuals block
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

        # derive cooldown in frames (60 FPS default)
        fps = getattr(config, "FPS", 60)
        cooldown_s = float(policy.get("cooldown_s", 10.0))
        cooldown_frames = int(round(cooldown_s * fps))
        # separate restart cooldown (falls back to cooldown_s)
        restart_cooldown_s = float(policy.get("restart_cooldown_s", cooldown_s))
        restart_cooldown_frames = int(round(restart_cooldown_s * fps))
        # fixed between-waves cooldown for mixed trigger mode (initial proximity only)
        between_waves_cooldown_s = float(policy.get("between_waves_cooldown_s", 0.0) or 0.0)
        between_waves_cooldown_frames = int(round(between_waves_cooldown_s * fps))

        # Convert zone-local tile -> global tile using zone offsets
        zone = inst.get("zone", "lobby")
        local_tx, local_ty = tuple(inst.get("tile", (0, 0)))
        off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
        anchor_tile = (off_x + int(local_tx), off_y + int(local_ty))

        return SpawnerConfig(
            template_id=tpl.get("id", "unknown"),
            zone=zone,
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
        )

    def update(self, world, camera=None):
        # Robust option: allow MenuManager to request an empty start without spawners
        # by setting a transient flag on the ECS world. If present, consume it and
        # skip this frame only, without marking the system as loaded. This allows
        # spawners to be placed on the very next frame without requiring a reload.
        try:
            if getattr(world, 'skip_spawners_on_first_load', False):
                try:
                    delattr(world, 'skip_spawners_on_first_load')
                except Exception:
                    pass
                return
        except Exception:
            pass
        # Only run once per map load
        if self._loaded:
            return
        self._loaded = True

        self._templates = self._load_templates()
        self._waves = self._load_waves()
        instances = self._load_instances()
        if not instances or not self._templates:
            return

        comps = world.components
        for inst in instances:
            tpl_id = inst.get("template_id")
            if not tpl_id or tpl_id not in self._templates:
                continue
            tpl = self._templates[tpl_id]
            cfg = self._resolve_config(tpl, inst)
            try:
                if getattr(config, 'DEBUG_SPAWNER', False):
                    logger.debug(f"[SpawnerPlacementSystem] update: creating spawner entity for inst_id={inst.get('id')} tpl={tpl_id} visuals_present={(cfg.state_visuals is not None)}")
            except Exception:
                pass
            eid = world.create_entity()
            comps['SpawnerConfig'][eid] = cfg
            st = SpawnerState()
            # Attach Phase 2 FSM set metadata for editor/overlay tools
            try:
                sid, params = self._compile_fsm_set(cfg)
                # Phase 3: apply optional overrides (instance > template)
                ov_sid, ov_params = self._fsm_override_from(tpl, inst)
                if ov_sid:
                    # Validate override set id if registry is available
                    if _fsm_get_set is not None:
                        try:
                            if _fsm_get_set(ov_sid) is not None:
                                sid = ov_sid
                            else:
                                logger.warning("[SpawnerPlacementSystem] Unknown FSM set override set_id='%s' (keeping compiled '%s')", ov_sid, sid)
                        except Exception:
                            sid = ov_sid  # best-effort if validation raised
                    else:
                        sid = ov_sid
                if ov_params:
                    try:
                        params.update(ov_params)
                    except Exception:
                        pass
                st.fsm_set_id = sid
                st.fsm_set_params = params
            except Exception:
                pass
            comps['SpawnerState'][eid] = st
            # Optional runtime visualization: link to an existing Building by building_id
            if getattr(cfg, 'visible_in_game', False) and getattr(cfg, 'building_id', None) is not None:
                inst_id = inst.get("id")
                blds = getattr(world, 'buildings', []) or []
                target = None
                # Prefer explicit building_id search
                try:
                    for ob in blds:
                        try:
                            if getattr(ob, 'id', None) == getattr(cfg, 'building_id', None):
                                target = ob
                                break
                        except Exception:
                            continue
                except Exception:
                    target = None
                # Fallback: match by existing spawn_id tag
                if target is None and inst_id is not None:
                    sid = str(inst_id)
                    for ob in blds:
                        try:
                            if getattr(ob, 'spawn_id', None) == sid:
                                target = ob
                                break
                        except Exception:
                            continue
                if target is not None:
                    try:
                        setattr(target, "_spawner_eid", eid)
                        setattr(target, "_world_ref", world)
                        setattr(target, "_is_spawner_visual", True)
                        if inst_id is not None:
                            setattr(target, "spawner_instance_id", str(inst_id))
                            setattr(target, "spawn_id", str(inst_id))
                    except Exception:
                        pass
