"""
SpawnerPlacementSystem: loads spawner templates and instances from JSON and creates ECS entities
with SpawnerConfig + SpawnerState components.
"""
from __future__ import annotations

import logging

from roguelike_engine.config import config
from roguelike_game.ecs.components.spawner.spawner_config import SpawnerConfig
from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState

from .placement.loaders import load_templates, load_waves, load_instances
from .placement.config_resolver import resolve_config
from .placement.fsm_meta import compile_fsm_set, fsm_override_from, validate_set_id
from .placement.visuals import auto_repair_state_visuals

logger = logging.getLogger(__name__)


class SpawnerPlacementSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._loaded = False
        self._world_loaded: str | None = None
        self._templates: dict = {}
        self._waves: dict = {}

    def update(self, world, camera=None):
        # World-awareness: if the active world changed since last load, purge and force reload
        try:
            from roguelike_engine.config.map_config import global_map_settings
            cur_world = getattr(global_map_settings, 'current_world', 'base')
        except Exception:
            cur_world = 'base'
        if self._world_loaded is not None and self._world_loaded != cur_world:
            try:
                comps = world.components
                # Remove NPCs created by spawners
                for eid in list(comps.get('SpawnerChild', {}).keys()):
                    world.remove_entity(eid)
                # Remove spawner entities (SpawnerConfig/SpawnerState holders)
                to_remove = set()
                for eid in list(comps.get('SpawnerConfig', {}).keys()):
                    to_remove.add(eid)
                for eid in list(comps.get('SpawnerState', {}).keys()):
                    to_remove.add(eid)
                for eid in to_remove:
                    world.remove_entity(eid)
            except Exception:
                pass
            # Force reload
            self._loaded = False
            self._templates = {}
            self._waves = {}
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

        # Load sources
        self._templates = load_templates()
        self._waves = load_waves()
        instances = load_instances()
        if not instances or not self._templates:
            self._world_loaded = cur_world
            return

        comps = world.components
        for inst in instances:
            tpl_id = inst.get("template_id")
            if not tpl_id or tpl_id not in self._templates:
                continue

            tpl = self._templates[tpl_id]
            cfg: SpawnerConfig = resolve_config(tpl, inst, self._waves)

            try:
                if getattr(config, 'DEBUG_SPAWNER', False):
                    logger.debug(
                        f"[SpawnerPlacementSystem] update: creating spawner entity for inst_id={inst.get('id')} "
                        f"tpl={tpl_id} visuals_present={(cfg.state_visuals is not None)}"
                    )
            except Exception:
                pass

            eid = world.create_entity()
            comps['SpawnerConfig'][eid] = cfg

            st = SpawnerState()
            # Build FSM metadata and apply overrides
            try:
                sid, params = compile_fsm_set(cfg)
                ov_sid, ov_params = fsm_override_from(tpl, inst)
                if ov_sid:
                    valid = validate_set_id(ov_sid)
                    if valid is None or valid is True:
                        sid = ov_sid
                    else:
                        logger.warning(
                            "[SpawnerPlacementSystem] Unknown FSM set override set_id='%s' (keeping compiled '%s')",
                            ov_sid,
                            sid,
                        )
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
                auto_repair_state_visuals(world, eid, cfg, inst)
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
        # Mark world of last successful placement
        self._world_loaded = cur_world
