from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Optional, Tuple

import pygame

from ...services.coords import screen_to_tile
from ...services.persistence import (
    find_instance_in_json,
    load_instances_json,
    load_spawners_json,
    write_instances_json,
    zone_for_global_tile,
)
from .mouse_left_common import LeftClickContext
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState
from roguelike_game.ecs.systems.spawner.placement.config_resolver import resolve_config
from roguelike_game.ecs.systems.spawner.placement.loaders import load_waves
from roguelike_game.ecs.systems.spawner.placement.visuals import auto_repair_state_visuals


@dataclass
class PlacementFlow:
    """Encapsulates the life-cycle of placing a new spawner instance."""

    ctx: LeftClickContext
    template_id: Any

    def execute(self) -> None:
        self.ctx.log_debug("[SpawnerEditor] Placement flow started for template %s", self.template_id)
        location = self._compute_location()
        if location is None:
            return
        zone, local_tile = location
        try:
            instance = self._persist_instance(zone, local_tile)
            self._instantiate_in_world(instance)
        finally:
            # Always restore UI state (cursor, flags) even if placement fails midway
            self._finalize_ui_state()

    def _compute_location(self) -> Optional[Tuple[str, Tuple[int, int]]]:
        camera = self.ctx.camera
        # Resolve screen_to_tile and zone_for_global_tile from mouse_left (monkeypatch-friendly)
        def _get(name, fallback):
            try:
                from . import mouse_left as _ml  # local import to avoid hard circularities at import time
                return getattr(_ml, name, fallback)
            except Exception:
                return fallback
        s2t = _get("screen_to_tile", screen_to_tile)
        zfg = _get("zone_for_global_tile", zone_for_global_tile)
        try:
            tx, ty = s2t(camera, self.ctx.mx, self.ctx.my)
        except Exception:  # noqa: BLE001 - defensive against camera issues
            self.ctx.log_debug("[SpawnerEditor] placement failed to compute tile", exc_info=True)
            return None
        zone = zfg(int(tx), int(ty)) or "lobby"
        off_x, off_y = global_map_settings.zone_offsets.get(str(zone), (0, 0))
        local = (int(tx - off_x), int(ty - off_y))
        self.ctx.log_debug(
            "[SpawnerEditor] placement resolved zone=%s local_tile=%s", zone, local
        )
        return str(zone), local

    def _persist_instance(self, zone: str, local_tile: Tuple[int, int]) -> Dict[str, Any]:
        payload = {
            "template_id": str(self.template_id),
            "zone": str(zone),
            "tile": [int(local_tile[0]), int(local_tile[1])],
        }
        # Resolve persistence fns via mouse_left when monkeypatched
        def _get(name, fallback):
            try:
                from . import mouse_left as _ml
                return getattr(_ml, name, fallback)
            except Exception:
                return fallback
        _load_instances = _get("load_instances_json", load_instances_json)
        _write_instances = _get("write_instances_json", write_instances_json)
        _find_instance = _get("find_instance_in_json", find_instance_in_json)

        records = self.ctx.guard("load_instances_json", _load_instances, default=[]) or []
        records.append(payload)
        self.ctx.guard("write_instances_json", lambda: _write_instances(records))
        refreshed = self.ctx.guard("load_instances_json", _load_instances, default=records) or records
        _, idx, _ = self.ctx.guard("find_instance_in_json", lambda: _find_instance(str(self.template_id), str(zone), tuple(local_tile)), default=(None, None, None))
        if isinstance(idx, int) and 0 <= idx < len(refreshed):
            instance = refreshed[idx]
        else:
            instance = payload
        self.ctx.log_debug("[SpawnerEditor] placement persisted instance=%s", instance)
        return instance

    def _instantiate_in_world(self, instance: Dict[str, Any]) -> None:
        world = self.ctx.world
        if world is None:
            return
        # Resolve loaders via mouse_left when monkeypatched
        def _get(name, fallback):
            try:
                from . import mouse_left as _ml
                return getattr(_ml, name, fallback)
            except Exception:
                return fallback
        _load_spawners = _get("load_spawners_json", load_spawners_json)
        _load_waves = _get("load_waves", load_waves)

        templates = self.ctx.guard("load_spawners_json", _load_spawners, default=None) or []
        template = next(
            (
                tpl
                for tpl in templates
                if str(tpl.get("id")) == str(self.template_id)
            ),
            None,
        )
        if template is None:
            self.ctx.log_debug("[SpawnerEditor] placement: template %s not found", self.template_id)
            return
        waves = self.ctx.guard("load_waves", _load_waves, default=None)
        if waves is None:
            waves = {}
        config = self.ctx.guard(
            "resolve_config",
            lambda: resolve_config(template, instance, waves),
            default=None,
        )
        if config is None:
            return
        try:
            eid = world.create_entity()
        except Exception:  # noqa: BLE001 - guard entity creation
            self.ctx.log_debug("[SpawnerEditor] placement failed to create entity", exc_info=True)
            return
        comps = getattr(world, "components", {})
        comps.setdefault("SpawnerConfig", {})[eid] = config
        comps.setdefault("SpawnerState", {})[eid] = SpawnerState()
        # Resolve auto_repair via mouse_left (monkeypatch-friendly)
        def _get(name, fallback):
            try:
                from . import mouse_left as _ml
                return getattr(_ml, name, fallback)
            except Exception:
                return fallback
        _auto_repair = _get("auto_repair_state_visuals", auto_repair_state_visuals)
        self.ctx.guard("auto_repair_state_visuals", lambda: _auto_repair(world, eid, config, instance))
        self.ctx.log_debug("[SpawnerEditor] placement created entity=%s", eid)

    def _finalize_ui_state(self) -> None:
        self.ctx.set_attr(self.ctx.model, "placing_template_id", None, "clear placing_template_id")
        controller_model = self.ctx.get_attr(self.ctx.controller, "model")
        self.ctx.set_attr(controller_model, "add_mode_active", False, "controller.model.add_mode_active")

        toolbar = self.ctx.get_attr(self.ctx.controller, "instance_toolbar")
        toolbar_model = self.ctx.get_attr(toolbar, "model")
        self.ctx.set_attr(toolbar_model, "add_mode_active", False, "instance_toolbar.add_mode_active")
        self.ctx.set_attr(toolbar_model, "add_templates", [], "instance_toolbar.add_templates")

        spawner_toolbar = self.ctx.get_attr(self.ctx.controller, "spawner_toolbar")
        spawner_toolbar_model = self.ctx.get_attr(spawner_toolbar, "model")
        self.ctx.set_attr(
            spawner_toolbar_model,
            "active_tool",
            "spawner_instances",
            "spawner_toolbar.active_tool",
        )

        self.ctx.refresh_instances_from_disk()
        self.ctx.world_state_set("spawner_input_suppressed", False)
        self.ctx.guard(
            "pygame.mouse.set_cursor",
            lambda: pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW),
        )
        self.ctx.log_debug("[SpawnerEditor] placement flow finalized UI state")


def handle_skip_first_placement_click(context: LeftClickContext) -> bool:
    model = context.model
    placing_tpl = getattr(model, "placing_template_id", None)
    skip = bool(getattr(model, "skip_first_placement_click", False))
    if placing_tpl and skip:
        context.set_attr(model, "skip_first_placement_click", False, "skip_first_placement_click")
        return True
    return False


def handle_placement_mode(context: LeftClickContext) -> bool:
    template_id = getattr(context.model, "placing_template_id", None)
    if not template_id:
        return False
    PlacementFlow(context, template_id).execute()
    return True
