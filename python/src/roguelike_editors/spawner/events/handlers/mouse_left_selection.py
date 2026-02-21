from __future__ import annotations

from typing import Tuple

from ...services.picking import pick_spawner_under_cursor
from .mouse_left_common import LeftClickContext
from .mouse_left_building_selection import select_building_under_cursor
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings


def handle_building_selection(context: LeftClickContext) -> bool:
    """Select a building under the cursor when none is selected yet."""
    if context.get_selected_building_id() is not None:
        return False
    bid = select_building_under_cursor(context)
    if bid is None:
        return False
    context.log_debug(
        "[SpawnerEditor] LMB selected building via early-pick: bid=%s", bid
    )
    return True


def handle_anchor_selection(context: LeftClickContext) -> bool:
    """Select a spawner anchor under the cursor and sync UI state."""
    world = context.world
    camera = context.camera
    if world is None or camera is None:
        return False
    eid = context.guard(
        "pick_spawner_under_cursor",
        lambda: pick_spawner_under_cursor(world, camera, context.mx, context.my),
    )
    if eid is None:
        return False

    context.set_attr(context.model, "selected_eid", eid, "model.selected_eid")
    context.world_state_set("spawner_selected_eid", eid)
    _recentre_camera(context, eid)
    _sync_instance_panel_selection(context, eid)
    context.clear_building_selection()
    return True


def handle_clear_selection(context: LeftClickContext) -> bool:
    """Clear spawner and building selection when clicking empty space."""
    if getattr(context.model, "selected_eid", None) is not None:
        context.set_attr(context.model, "selected_eid", None, "model.selected_eid")
    context.world_state_set("spawner_selected_eid", None)
    bid = select_building_under_cursor(context)
    if bid is not None:
        return True
    context.clear_building_selection()
    return False


def _recentre_camera(context: LeftClickContext, eid: int) -> None:
    world = context.world
    camera = context.camera
    if world is None or camera is None:
        return
    config = context.guard(
        "get SpawnerConfig",
        lambda: world.components["SpawnerConfig"][eid],
    )
    if config is None:
        return
    tx, ty = getattr(config, "anchor_tile", (0, 0))
    zoom = float(getattr(camera, "zoom", 1.0) or 1.0)
    x_px = (float(tx) + 0.5) * float(TILE_SIZE)
    y_px = (float(ty) + 0.5) * float(TILE_SIZE)
    sw = float(getattr(camera, "screen_width", 0) or 0)
    sh = float(getattr(camera, "screen_height", 0) or 0)
    context.guard(
        "camera recentre",
        lambda: (
            setattr(camera, "offset_x", x_px - (sw / (2.0 * zoom))),
            setattr(camera, "offset_y", y_px - (sh / (2.0 * zoom))),
        ),
    )


def _sync_instance_panel_selection(context: LeftClickContext, eid: int) -> None:
    world = context.world
    controller = context.controller
    if world is None or controller is None:
        return
    cfg = context.guard(
        "world.components['SpawnerConfig'][eid]",
        lambda: world.components["SpawnerConfig"][eid],
    )
    if cfg is None:
        return
    tpl = str(getattr(cfg, "template_id", ""))
    zone = str(getattr(cfg, "zone", "lobby"))
    tx, ty = getattr(cfg, "anchor_tile", (0, 0))
    local = _compute_local_tile(zone, (tx, ty))
    instances_panel = context.get_attr(controller, "spawner_instances")
    context.guard(
        "instances_panel.select_by_tpl_zone_tile",
        lambda: instances_panel.select_by_tpl_zone_tile(tpl, zone, local)
        if instances_panel is not None
        else None,
    )
    toolbar = context.get_attr(controller, "spawner_toolbar")
    toolbar_model = context.get_attr(toolbar, "model")
    context.set_attr(toolbar_model, "active_tool", "spawner_instances", "spawner_toolbar.active_tool")


def _compute_local_tile(zone: str, anchor_tile: Tuple[int, int]) -> Tuple[int, int]:
    offsets = global_map_settings.zone_offsets.get(zone, (0, 0))
    return int(anchor_tile[0] - int(offsets[0])), int(anchor_tile[1] - int(offsets[1]))
