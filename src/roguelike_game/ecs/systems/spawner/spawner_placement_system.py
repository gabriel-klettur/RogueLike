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
from roguelike_engine.config.config import BUILDINGS_INSTANCES_PATH, BUILDINGS_TEMPLATES_PATH
import pygame

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
        # Runtime cache for saved positions keyed by spawner instance id
        self._positions: Dict[str, Dict[str, Any]] | None = None
        # Track created instance ids to avoid duplicates caused by repeated entries in instances JSON
        self._seen_instance_ids: set[str] = set()

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

    def _load_positions(self) -> Dict[str, Dict[str, Any]]:
        """Load saved NPC positions keyed by spawner instance id.
        File shape: { "<inst_id>": { "zone": str, "tile": [tx, ty] }, ... }
        """
        base = config.DATA_DIR
        path = os.path.join(base, "spawners", "spawners_positions.json")
        try:
            with open(path, "r", encoding="utf-8-sig") as f:
                data = json.load(f)
            return data if isinstance(data, dict) else {}
        except FileNotFoundError:
            return {}
        except Exception:
            return {}

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

        # -------------------- life defaults & hp scope (instance-level) --------------------
        life_defaults: Dict[str, Any] | None = None
        hp_scope: str = str(inst.get('overrides', {}).get('hp_scope', 'per_state')).strip().lower() if isinstance(inst.get('overrides'), dict) else 'per_state'
        try:
            if isinstance(inst.get('overrides'), dict):
                ld = inst['overrides'].get('life_defaults')
                if isinstance(ld, dict):
                    # Normalize known keys and types; leave unknowns as-is for forward-compat
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
        except Exception:
            life_defaults = life_defaults or None

        # -------------------- apply overrides in dot-notation --------------------
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

        # Prefer saved position for this instance id, if available
        inst_id = inst.get("id")
        zone = inst.get("zone", "lobby")
        local_tx, local_ty = tuple(inst.get("tile", (0, 0)))
        try:
            if inst_id:
                if self._positions is None:
                    self._positions = self._load_positions()
                saved = self._positions.get(str(inst_id)) if isinstance(self._positions, dict) else None
                if isinstance(saved, dict):
                    sz = saved.get("zone")
                    st = saved.get("tile")
                    if isinstance(sz, str) and isinstance(st, (list, tuple)) and len(st) >= 2:
                        zone = sz
                        local_tx, local_ty = int(st[0]), int(st[1])
        except Exception:
            pass
        # Convert zone-local tile -> global tile using zone offsets
        off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
        anchor_tile = (off_x + int(local_tx), off_y + int(local_ty))

        # If there are per-state visuals defined (from template or instance), enable in-game visuals by default
        # unless explicitly disabled by overrides earlier. This ensures runtime show/hide per state works out-of-the-box.
        try:
            if not visible_in_game and state_visuals:
                visible_in_game = True
        except Exception:
            pass

        # Collect optional per-state pixel offsets for visuals (relative to spawner center, zone-relative px)
        visuals_offsets_px: Dict[str, tuple[int, int]] = {}
        try:
            ivis = inst.get("visuals")
            if isinstance(ivis, dict):
                for sk, sv in ivis.items():
                    try:
                        # New format: dict {instance_id, template_id}
                        if isinstance(sv, dict):
                            off = sv.get('offset')
                            if isinstance(off, (list, tuple)) and len(off) == 2:
                                dx, dy = int(off[0]), int(off[1])
                                # Normalize key both as-is and lowercase runtime token for convenience
                                visuals_offsets_px[str(sk).strip().lower()] = (dx, dy)
                        else:
                            # legacy int id mapping
                            pass
                    except Exception:
                        pass
        except Exception:
            pass

        # -------------------- Collect per-state life config from visuals[*].life --------------------
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
                            if eff:
                                visuals_life[key_norm] = eff
                    except Exception:
                        continue
        except Exception:
            visuals_life = visuals_life or {}

        return SpawnerConfig(
            template_id=tpl.get("id", ""),
            zone=str(zone),
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
            instance_id=str(inst_id) if inst_id is not None else None,
        )

    # --- Auto-repair helpers -------------------------------------------------
    def _load_buildings_instances_json(self) -> list[dict]:
        try:
            with open(BUILDINGS_INSTANCES_PATH, 'r', encoding='utf-8-sig') as f:
                data = json.load(f)
            return data if isinstance(data, list) else []
        except FileNotFoundError:
            return []
        except Exception:
            return []

    def _write_buildings_instances_json(self, arr: list[dict]) -> None:
        # Stable order by id
        try:
            arr.sort(key=lambda e: int(e.get('id') or 0))
        except Exception:
            pass
        os.makedirs(os.path.dirname(BUILDINGS_INSTANCES_PATH), exist_ok=True)
        with open(BUILDINGS_INSTANCES_PATH, 'w', encoding='utf-8') as f:
            json.dump(arr or [], f, ensure_ascii=False, indent=4)

    def _load_buildings_templates_json(self) -> list[dict]:
        try:
            with open(BUILDINGS_TEMPLATES_PATH, 'r', encoding='utf-8-sig') as f:
                data = json.load(f)
            return data if isinstance(data, list) else []
        except FileNotFoundError:
            return []
        except Exception:
            return []

    def _get_template_image_path(self, templates: list[dict], template_id: int) -> str | None:
        for t in templates:
            try:
                if int(t.get('id')) == int(template_id):
                    assets = t.get('assets') if isinstance(t.get('assets'), dict) else {}
                    path = assets.get('idle') or assets.get('image') or t.get('image')
                    return str(path) if path else None
            except Exception:
                continue
        return None

    def _calc_centered_rel(self, local_tile: tuple[int, int], tpl_entry: dict | None, img_path: str | None) -> tuple[int, int, tuple[int, int] | None]:
        # Base rel from tile top-left
        rel_x = int(local_tile[0] * TILE_SIZE)
        rel_y = int(local_tile[1] * TILE_SIZE)
        spawn_cx = int(rel_x + (TILE_SIZE // 2))
        spawn_cy = int(rel_y + (TILE_SIZE // 2))
        # Desired scale
        w = h = None
        try:
            if isinstance(tpl_entry, dict) and isinstance(tpl_entry.get('original_scale'), (list, tuple)):
                oscale = tpl_entry['original_scale']
                if len(oscale) >= 2:
                    w, h = int(oscale[0]), int(oscale[1])
        except Exception:
            w = h = None
        br = None
        if img_path:
            try:
                surf = pygame.image.load(img_path)
                if w is not None and h is not None and w > 0 and h > 0:
                    surf = pygame.transform.scale(surf, (int(w), int(h)))
                br = surf.get_bounding_rect(min_alpha=1)
            except Exception:
                br = None
                # best effort: infer scale from image if none provided
                if w is None or h is None:
                    try:
                        iw, ih = surf.get_size()  # type: ignore[name-defined]
                        w, h = int(iw), int(ih)
                    except Exception:
                        w = h = None
        # Center either bounding rect or full image
        try:
            if br is not None and br.w > 0 and br.h > 0:
                rel_x = int(spawn_cx - (br.x + br.w // 2))
                rel_y = int(spawn_cy - (br.y + br.h // 2))
            elif w is not None and h is not None and w > 0 and h > 0:
                rel_x = int(spawn_cx - (w // 2))
                rel_y = int(spawn_cy - (h // 2))
        except Exception:
            pass
        scale = (int(w), int(h)) if (w is not None and h is not None and w > 0 and h > 0) else None
        return rel_x, rel_y, scale

    def _append_building_object_in_world(self, world, inst_entry: dict, tpl_entry: dict | None, img_path: str | None) -> None:
        try:
            rel_x = int(inst_entry.get('rel_x') or 0)
            rel_y = int(inst_entry.get('rel_y') or 0)
            image_path = img_path or ''
            solid = True
            split_ratio = 0.5
            z_bottom = None
            z_top = None
            scale = None
            if isinstance(tpl_entry, dict):
                solid = bool(tpl_entry.get('solid', True))
                try:
                    split_ratio = float(tpl_entry.get('split_ratio', 0.5))
                except Exception:
                    split_ratio = 0.5
                z_bottom = tpl_entry.get('z_bottom')
                z_top = tpl_entry.get('z_top')
            try:
                if isinstance(inst_entry.get('overrides'), dict) and isinstance(inst_entry['overrides'].get('scale'), (list, tuple)):
                    sc = inst_entry['overrides']['scale']
                    if len(sc) >= 2:
                        scale = (int(sc[0]), int(sc[1]))
            except Exception:
                scale = None
            # Apply per-instance overrides
            try:
                if isinstance(inst_entry.get('overrides'), dict) and (inst_entry['overrides'].get('split_ratio') is not None):
                    try:
                        sr = float(inst_entry['overrides']['split_ratio'])
                        # clamp to safe range
                        split_ratio = max(0.05, min(sr, 0.95))
                    except Exception:
                        pass
            except Exception:
                pass

            b = Building(
                rel_x=rel_x,
                rel_y=rel_y,
                image_path=image_path,
                solid=solid,
                scale=scale,
                split_ratio=split_ratio,
                z_bottom=z_bottom,
                z_top=z_top,
            )
            # Bind identifiers and zone
            try:
                setattr(b, 'id', inst_entry.get('id'))
            except Exception:
                pass
            try:
                setattr(b, 'zone', inst_entry.get('zone'))
            except Exception:
                pass
            # Tag as spawner visual for renderer/debug
            try:
                setattr(b, '_is_spawner_visual', True)
                sid = inst_entry.get('spawner_instance_id') or (inst_entry.get('overrides') or {}).get('spawner_instance_id')
                if sid is not None:
                    setattr(b, 'spawner_instance_id', sid)
                    setattr(b, 'spawn_id', sid)
            except Exception:
                pass
            # If this building is linked to a spawner instance (has sid), prefer split_ratio from spawners_instances.json visuals mapping
            try:
                sid_val = inst_entry.get('spawner_instance_id') or (inst_entry.get('overrides') or {}).get('spawner_instance_id') or inst_entry.get('spawn_id')
                bid_val = inst_entry.get('id')
                if sid_val is not None and bid_val is not None:
                    try:
                        sid_str = str(sid_val)
                        bid_int = int(bid_val)
                    except Exception:
                        sid_str = None
                        bid_int = None
                    if sid_str is not None and bid_int is not None:
                        # Load spawners instances and find matching visuals entry
                        for inst in (self._load_instances() or []):
                            try:
                                if str(inst.get('id')) != sid_str:
                                    continue
                                vis = inst.get('visuals') if isinstance(inst.get('visuals'), dict) else {}
                                for _, v in list(vis.items()):
                                    try:
                                        if isinstance(v, dict):
                                            vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                        else:
                                            vid = int(v)
                                    except Exception:
                                        vid = None
                                    if vid is not None and int(vid) == int(bid_int):
                                        # Found mapping for this building; apply split_ratio if provided
                                        try:
                                            if isinstance(v, dict) and (v.get('split_ratio') is not None):
                                                sr = float(v.get('split_ratio'))
                                                # Match editor clamp range to avoid invisible handles
                                                sr = max(0.05, min(sr, 0.95))
                                                b.split_ratio = float(sr)
                                        except Exception:
                                            pass
                                        raise StopIteration
                                raise StopIteration
                            except StopIteration:
                                break
                            except Exception:
                                continue
            except Exception:
                pass
            # Append to world
            try:
                if getattr(world, 'buildings', None) is None:
                    world.buildings = []
            except Exception:
                pass
            try:
                # Avoid duplicate objects with same id
                for ob in getattr(world, 'buildings', []) or []:
                    if getattr(ob, 'id', None) == inst_entry.get('id'):
                        return
                world.buildings.append(b)
            except Exception:
                pass
        except Exception:
            pass

    def _persist_spawner_instance_visuals(self, inst_id: str | None, visuals: dict, ensure_visible_in_game: bool = True) -> None:
        if not inst_id:
            return
        base = config.DATA_DIR
        path = os.path.join(base, "spawners", "spawners_instances.json")
        try:
            with open(path, 'r', encoding='utf-8-sig') as f:
                data = json.load(f)
            if not isinstance(data, list):
                return
        except FileNotFoundError:
            return
        except Exception:
            return
        changed = False
        for i, e in enumerate(data):
            try:
                if str(e.get('id')) == str(inst_id):
                    # write visuals dict back
                    if e.get('visuals') != visuals:
                        e['visuals'] = visuals
                        changed = True
                    if ensure_visible_in_game:
                        ov = dict(e.get('overrides') or {})
                        if not bool(ov.get('visible_in_game', False)):
                            ov['visible_in_game'] = True
                            e['overrides'] = ov
                            changed = True
                    break
            except Exception:
                continue
        if changed:
            try:
                with open(path, 'w', encoding='utf-8') as f:
                    json.dump(data, f, ensure_ascii=False, indent=4)
            except Exception:
                pass

    def _auto_repair_state_visuals(self, world, eid: int, cfg: SpawnerConfig, inst: dict) -> None:
        """Ensure that for any visuals mapping that has a valid template_id but missing/nonexistent instance_id,
        we create a Building instance on disk and in-memory, update cfg.state_visuals, and persist visuals back to spawners_instances.json.
        """
        vis = inst.get('visuals') if isinstance(inst, dict) else None
        if not isinstance(vis, dict) or not vis:
            return
        # Load buildings data and templates
        b_arr = self._load_buildings_instances_json()
        existing_ids = set()
        max_id = 0
        for e in b_arr:
            try:
                eid_ = int(e.get('id'))
                existing_ids.add(eid_)
                if eid_ > max_id:
                    max_id = eid_
            except Exception:
                continue
        templates = self._load_buildings_templates_json()
        tmap = {}
        for t in templates:
            try:
                tmap[int(t.get('id'))] = t
            except Exception:
                continue
        # Determine zone and local tile for placement
        try:
            zone = str(inst.get('zone')) if inst.get('zone') is not None else 'lobby'
        except Exception:
            zone = 'lobby'
        try:
            local_tile = inst.get('tile') or (0, 0)
            local_tile = (int(local_tile[0]), int(local_tile[1]))
        except Exception:
            local_tile = (0, 0)

        # Track updated visuals to optionally persist
        updated_visuals = False
        # Ensure cfg.state_visuals is a dict we can mutate
        if getattr(cfg, 'state_visuals', None) is None:
            try:
                cfg.state_visuals = {}
            except Exception:
                pass

        for key, val in list(vis.items()):
            # Parse instance_id and template_id from mapping
            cur_iid = None
            tpl_id = None
            # Optional visuals-provided scale override
            visuals_scale: tuple[int, int] | None = None
            if isinstance(val, dict):
                try:
                    cur_iid = int(val.get('instance_id') or val.get('id') or val.get('building_instance_id'))
                except Exception:
                    cur_iid = None
                try:
                    tpl_id = int(val.get('template_id')) if val.get('template_id') is not None else None
                except Exception:
                    tpl_id = None
                # Preserve optional offset if present
                try:
                    off = val.get('offset')
                    if isinstance(off, (list, tuple)) and len(off) == 2:
                        dx, dy = int(off[0]), int(off[1])
                        # Normalize key both as-is and lowercase runtime token for convenience
                        try:
                            if getattr(cfg, 'visuals_offsets_px', None) is None:
                                cfg.visuals_offsets_px = {}
                        except Exception:
                            pass
                        try:
                            cfg.visuals_offsets_px[str(key).strip().lower()] = (dx, dy)
                        except Exception:
                            pass
                    # Read optional scale override from visuals mapping
                    sc = val.get('scale')
                    if isinstance(sc, (list, tuple)) and len(sc) == 2:
                        try:
                            sw, sh = int(sc[0]), int(sc[1])
                            if sw > 0 and sh > 0:
                                visuals_scale = (sw, sh)
                        except Exception:
                            visuals_scale = None
                except Exception:
                    pass
            else:
                # legacy int id mapping
                try:
                    cur_iid = int(val)
                except Exception:
                    cur_iid = None

            # If instance id exists, keep and normalize cfg mapping
            if cur_iid is not None and cur_iid in existing_ids:
                try:
                    cfg.state_visuals[str(key)] = int(cur_iid)
                except Exception:
                    pass
                # If visuals provided a scale override, persist it into the existing building instance
                if visuals_scale is not None:
                    try:
                        changed_bi = False
                        for e in b_arr:
                            try:
                                if int(e.get('id')) != int(cur_iid):
                                    continue
                            except Exception:
                                continue
                            ov = e.get('overrides') or {}
                            if not isinstance(ov, dict):
                                ov = {}
                            try:
                                cur_sc = ov.get('scale')
                                cur_sc_t = (int(cur_sc[0]), int(cur_sc[1])) if isinstance(cur_sc, (list, tuple)) and len(cur_sc) == 2 else None
                            except Exception:
                                cur_sc_t = None
                            if cur_sc_t != visuals_scale:
                                ov['scale'] = [int(visuals_scale[0]), int(visuals_scale[1])]
                                e['overrides'] = ov
                                changed_bi = True
                            break
                        if changed_bi:
                            try:
                                self._write_buildings_instances_json(b_arr)
                            except Exception:
                                logger.warning("[SpawnerPlacementSystem] Could not persist scale override for existing building instance")
                    except Exception:
                        pass
                continue
            # Need to create if we have a valid template id
            if tpl_id is None or tpl_id not in tmap:
                # Without a template we cannot repair; skip
                continue
            # Compute placement and optional scale
            tpl_entry = tmap.get(tpl_id)
            img_path = self._get_template_image_path(templates, tpl_id)
            rel_x, rel_y, scale = self._calc_centered_rel(local_tile, tpl_entry, img_path)
            new_id = max_id + 1
            max_id = new_id
            entry = {
                'id': int(new_id),
                'template_id': int(tpl_id),
                'zone': zone,
                'rel_x': int(rel_x),
                'rel_y': int(rel_y),
                'overrides': {
                    '_is_spawner_visual': True,
                },
                'spawn_id': str(inst.get('id')) if inst.get('id') is not None else None,
                'spawner_instance_id': str(inst.get('id')) if inst.get('id') is not None else None,
            }
            # Prefer visuals-provided scale over template-derived one
            if visuals_scale is not None:
                try:
                    entry['overrides']['scale'] = [int(visuals_scale[0]), int(visuals_scale[1])]  # type: ignore[index]
                except Exception:
                    pass
            elif scale is not None:
                try:
                    entry['overrides']['scale'] = [int(scale[0]), int(scale[1])]  # type: ignore[index]
                except Exception:
                    pass
            # Propagate spawner_instance_id into overrides as well
            try:
                if inst.get('id') is not None:
                    entry['overrides']['spawner_instance_id'] = str(inst.get('id'))
            except Exception:
                pass
            # Append to buildings_instances.json and persist
            b_arr.append(entry)
            try:
                self._write_buildings_instances_json(b_arr)
                existing_ids.add(int(new_id))
            except Exception:
                logger.warning("[SpawnerPlacementSystem] Could not persist buildings_instances for auto-repair")
            # Ensure it exists in memory for immediate visibility
            self._append_building_object_in_world(world, entry, tpl_entry, img_path)
            # Update cfg/state + inst visuals mapping in-memory (preserve any existing offset field if present)
            try:
                cfg.state_visuals[str(key)] = int(new_id)
            except Exception:
                pass
            try:
                preserved_offset = None
                try:
                    if isinstance(val, dict) and isinstance(val.get('offset'), (list, tuple)) and len(val.get('offset')) == 2:
                        preserved_offset = [int(val['offset'][0]), int(val['offset'][1])]
                except Exception:
                    preserved_offset = None
                # Preserve any existing fields from visuals mapping (e.g., 'scale', 'split_ratio', etc.)
                # when we create the new mapping. Start from the existing dict if present.
                if isinstance(val, dict):
                    entry_map = dict(val)
                else:
                    entry_map = {}
                entry_map['instance_id'] = int(new_id)
                entry_map['template_id'] = int(tpl_id)
                if preserved_offset is not None:
                    entry_map['offset'] = preserved_offset  # type: ignore[index]
                vis[str(key)] = entry_map
                updated_visuals = True
            except Exception:
                pass

            # Ensure this spawner will try to render visuals
            try:
                if not getattr(cfg, 'visible_in_game', False):
                    cfg.visible_in_game = True
            except Exception:
                pass
        # Persist spawner visuals if we changed them
        if updated_visuals:
            try:
                self._persist_spawner_instance_visuals(str(inst.get('id')) if inst.get('id') is not None else None, vis, ensure_visible_in_game=True)
            except Exception:
                pass

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
            inst_id = inst.get("id")
            # Skip duplicate instance ids defensively
            try:
                if inst_id is not None:
                    key = str(inst_id)
                    if key in self._seen_instance_ids:
                        logger.warning("[SpawnerPlacementSystem] Skipping duplicate spawner instance id=%s (already created)", key)
                        continue
                    self._seen_instance_ids.add(key)
            except Exception:
                pass
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
            # Auto-repair: ensure visual building instances exist for this spawner
            try:
                self._auto_repair_state_visuals(world, eid, cfg, inst)
            except Exception:
                # Never break game start on optional repair
                logger.exception("[SpawnerPlacementSystem] auto-repair visuals failed", exc_info=False)
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
