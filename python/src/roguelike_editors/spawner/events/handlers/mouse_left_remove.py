from __future__ import annotations

from typing import Any

from ...services.picking import pick_spawner_under_cursor
from ..types import EditorCtx
from .mouse_left_common import LeftClickContext
from roguelike_engine.config.map_config import global_map_settings


def handle_remove_mode(context: LeftClickContext) -> bool:
    """Prepare delete confirmation payload when Remove Mode is active."""
    model = context.model
    if not bool(getattr(model, "remove_mode_active", False)):
        return False

    context.log_debug("[SpawnerEditor] RemoveMode LMB at (%s,%s)", context.mx, context.my)

    world = context.world
    camera = context.camera
    if world is None or camera is None:
        return True

    # Resolve picker via mouse_left (monkeypatch-friendly)
    def _get(name, fallback):
        try:
            from . import mouse_left as _ml
            return getattr(_ml, name, fallback)
        except Exception:
            return fallback
    _picker = _get("pick_spawner_under_cursor", pick_spawner_under_cursor)

    eid = context.guard(
        "pick_spawner_under_cursor",
        lambda: _picker(world, camera, context.mx, context.my),
    )
    if eid is None:
        context.log_debug("[SpawnerEditor] RemoveMode no candidate under cursor")
        return False

    cfg = _safe_get_spawner_config(world, eid)
    if cfg is None:
        return True

    zone = getattr(cfg, "zone", "lobby") or "lobby"
    tx, ty = getattr(cfg, "anchor_tile", (0, 0))
    off_x, off_y = global_map_settings.zone_offsets.get(str(zone), (0, 0))
    local = (int(tx - off_x), int(ty - off_y))

    payload = {
        "eid": eid,
        "template_id": str(getattr(cfg, "template_id", "")),
        "zone": str(zone),
        "local_tile": local,
    }
    context.set_attr(model, "pending_delete_confirm", payload, "pending_delete_confirm")
    context.world_state_set("spawner_remove_candidate_eid", eid)
    context.world_state_set("spawner_input_suppressed", True)

    controller_model = context.get_attr(context.controller, "model")
    context.set_attr(
        controller_model,
        "tutorial_delete_pending_pulse",
        True,
        "controller.model.tutorial_delete_pending_pulse",
    )
    context.log_debug("[SpawnerEditor] RemoveMode pending_delete_confirm prepared: %s", payload)
    return True


def _safe_get_spawner_config(world: Any, eid: int) -> Any:
    components = getattr(world, "components", {})
    configs = components.get("SpawnerConfig", {})
    if eid not in configs:
        return None
    return configs[eid]
