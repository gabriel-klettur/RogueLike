from __future__ import annotations

from typing import Any
import pygame

from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
)
from .types import EditorCtx


def start_resize(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """Begin resize mode for the currently selected building.
    Records origin and initial size on the model, suppresses gameplay input.
    """
    model = ctx.model
    ip = getattr(ctx.controller, 'instance_properties', None)
    sel_bid = None
    try:
        vmodel = getattr(getattr(ip, 'model', None), 'visuals', None) if ip else None
        sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
    except Exception:
        sel_bid = None
    if sel_bid is None:
        return False
    ob = None
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
    except Exception:
        ob = None
    if ob is None:
        return False
    try:
        mx, my = event.pos
    except Exception:
        mx = my = 0
    try:
        w0, h0 = ob.image.get_size()
    except Exception:
        return False
    # Set resize flags and context
    try:
        model.resizing_visual = True
        model.resizing_visual_bid = int(sel_bid)
        model.resize_origin = (int(mx), int(my))
        model.initial_size = (int(w0), int(h0))
        if hasattr(ctx.world, 'state'):
            setattr(ctx.world.state, 'spawner_input_suppressed', True)
    except Exception:
        pass
    return True


def update_resize_motion(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """Update size while in resize mode, emulating Building Editor logic."""
    model = ctx.model
    if not getattr(model, 'resizing_visual', False):
        return False
    bid = getattr(model, 'resizing_visual_bid', None)
    if bid is None:
        return False
    ip = getattr(ctx.controller, 'instance_properties', None)
    ob = None
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals._find_building_entity_by_id(int(bid))
    except Exception:
        ob = None
    if ob is None:
        return False
    try:
        mx, my = event.pos
    except Exception:
        return False
    start = getattr(model, 'resize_origin', None) or (mx, my)
    w0, h0 = getattr(model, 'initial_size', (None, None))
    if w0 is None or h0 is None:
        try:
            w0, h0 = ob.image.get_size()
        except Exception:
            return False
    dx = int(mx) - int(start[0])
    dy = int(my) - int(start[1])
    delta = max(dx, dy)
    aspect = (w0 / h0) if h0 else 1.0
    new_w = max(50, int(w0 + delta))
    new_h = max(50, int(new_w / aspect))
    try:
        cur_size = ob.image.get_size()
    except Exception:
        cur_size = None
    try:
        ob.resize(int(new_w), int(new_h))
    except Exception:
        return False
    # Pulse feedback similar to Building Editor when size changes
    try:
        if cur_size is not None and (int(new_w), int(new_h)) != cur_size:
            setattr(ctx.controller.model, 'tutorial_resized_pulse', True)
    except Exception:
        pass
    return True


def finish_resize(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """Persist size overrides on LMB up after resizing a visual building."""
    model = ctx.model
    world = ctx.world
    model.resizing_visual = False
    bid = getattr(model, 'resizing_visual_bid', None)
    model.resizing_visual_bid = None
    try:
        if hasattr(world, 'state'):
            setattr(world.state, 'spawner_input_suppressed', False)
    except Exception:
        pass
    if bid is None:
        return True
    try:
        data = svc_load_buildings_instances()
    except Exception:
        data = []
    changed = False
    for e in data or []:
        try:
            if int(e.get('id')) != int(bid):
                continue
        except Exception:
            continue
        # Infer current size from the world entity
        try:
            ip = getattr(ctx.controller, 'instance_properties', None)
            ob = ip.visuals._find_building_entity_by_id(int(bid))
            cur_w, cur_h = ob.image.get_size()
        except Exception:
            cur_w = cur_h = None
        if cur_w is not None and cur_h is not None:
            ov = e.get('overrides') or {}
            if not isinstance(ov, dict):
                ov = {}
            ov['scale'] = [int(cur_w), int(cur_h)]
            e['overrides'] = ov
            changed = True
            break
    if changed:
        try:
            svc_write_buildings_instances(data)
        except Exception:
            pass
    return True
