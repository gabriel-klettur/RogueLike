from __future__ import annotations

from typing import Any, Optional
import pygame

from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
)
from .utils import find_building_in_world_by_id, log_info_safe
from .types import EditorCtx


def propagate_split_ratio(ctx: EditorCtx, source_ob: Any, ratio: float, ip: Any) -> None:
    world = ctx.world
    # 1) Propagate by spawner instance id
    try:
        sid = None
        try:
            if ip is not None and hasattr(ip, 'visuals') and hasattr(ip.visuals, '_get_selected_spawner_id'):
                sid = ip.visuals._get_selected_spawner_id()
        except Exception:
            sid = None
        if world is not None and sid is not None:
            for ob2 in getattr(world, 'buildings', []) or []:
                try:
                    s2 = str(getattr(ob2, 'spawner_instance_id', getattr(ob2, 'spawn_id', '')))
                    if s2 != str(sid):
                        continue
                    if getattr(ob2, 'id', None) == getattr(source_ob, 'id', None):
                        continue
                    try:
                        ob2.split_ratio = float(ratio)
                    except Exception:
                        pass
                    try:
                        if getattr(ob2, 'controller', None) is not None:
                            ob2.controller.update_on_camera_change()
                    except Exception:
                        pass
                except Exception:
                    continue
    except Exception:
        pass
    # 2) Fallback by (zone, rel_x, rel_y)
    try:
        key = getattr(ctx.model, '_split_propagation_key', None)
        if world is not None and key is not None:
            kz, kx, ky = key
            for ob3 in getattr(world, 'buildings', []) or []:
                try:
                    mz = getattr(getattr(ob3, 'model', ob3), 'zone', None) or 'lobby'
                    mxr = int(getattr(getattr(ob3, 'model', ob3), 'rel_x', -999999))
                    myr = int(getattr(getattr(ob3, 'model', ob3), 'rel_y', -999999))
                    if str(mz) != str(kz) or int(mxr) != int(kx) or int(myr) != int(ky):
                        continue
                    if getattr(ob3, 'id', None) == getattr(source_ob, 'id', None):
                        continue
                    try:
                        ob3.split_ratio = float(ratio)
                    except Exception:
                        pass
                    try:
                        if getattr(ob3, 'controller', None) is not None:
                            ob3.controller.update_on_camera_change()
                    except Exception:
                        pass
                except Exception:
                    continue
    except Exception:
        pass


def begin_split_drag(ctx: EditorCtx, bid: int, event: pygame.event.Event) -> bool:
    model = ctx.model
    ip = getattr(ctx.controller, 'instance_properties', None)
    try:
        model.split_drag_active = True
        model.split_drag_bid = int(bid)
        setattr(ctx.controller.events, '_split_drag_first_logged', False)
    except Exception:
        model.split_drag_active = False
        model.split_drag_bid = None
    if not getattr(model, 'split_drag_active', False):
        return False
    # Resolve building entity in visuals or world
    ob = None
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals._find_building_entity_by_id(int(bid))
    except Exception:
        ob = None
    if ob is None:
        ob = find_building_in_world_by_id(ctx.world, int(bid))
    if ob is None or ctx.split_tool is None:
        return False
    try:
        ctx.split_adapter.selected_building = ob
        ctx.split_adapter.split_dragging = True
        ctx.split_tool.start_drag(ob)
        # Cache propagation key (zone, rel_x, rel_y)
        try:
            oz = getattr(getattr(ob, 'model', ob), 'zone', None) or 'lobby'
            orx = int(getattr(getattr(ob, 'model', ob), 'rel_x', 0))
            ory = int(getattr(getattr(ob, 'model', ob), 'rel_y', 0))
            setattr(model, '_split_propagation_key', (str(oz), int(orx), int(ory)))
        except Exception:
            setattr(model, '_split_propagation_key', None)
        # Log start
        try:
            mx, my = event.pos
        except Exception:
            mx = my = 0
        try:
            sr = getattr(ob, 'split_ratio', None)
            log_info_safe(ctx.logger, "SpawnerEditor: split drag START bid=%s ratio=%s mouse=(%s,%s)", str(bid), f"{sr:.3f}" if isinstance(sr, (int, float)) else str(sr), int(mx), int(my))
        except Exception:
            pass
    except Exception:
        return False
    # Suppress gameplay input during split drag
    try:
        if hasattr(ctx.world, 'state'):
            setattr(ctx.world.state, 'spawner_input_suppressed', True)
    except Exception:
        pass
    return True


def update_split_drag(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    ip = getattr(ctx.controller, 'instance_properties', None)
    cam = ctx.camera
    model = ctx.model
    try:
        bid = int(model.split_drag_bid)
    except Exception:
        return False
    # Resolve ob
    ob = None
    try:
        if ip is not None and hasattr(ip, 'visuals'):
            ob = ip.visuals._find_building_entity_by_id(int(bid))
    except Exception:
        ob = None
    if ob is None:
        ob = find_building_in_world_by_id(ctx.world, int(bid))
    if ob is None or cam is None or getattr(ob, 'image', None) is None:
        return False
    try:
        if ctx.split_tool is not None:
            ctx.split_adapter.selected_building = ob
            ctx.split_adapter.split_dragging = True
            ctx.split_tool.update_drag(event.pos, cam)
            # Mirror to world entity if different
            try:
                world_ob = find_building_in_world_by_id(ctx.world, int(bid))
            except Exception:
                world_ob = None
            if world_ob is not None and world_ob is not ob:
                try:
                    world_ob.split_ratio = float(getattr(ob, 'split_ratio', getattr(world_ob, 'split_ratio', 0.5)))
                except Exception:
                    pass
                try:
                    mh = int(getattr(getattr(world_ob, 'model', None), 'image', getattr(world_ob, 'image', None)).get_height())
                    setattr(world_ob.model, '_cut_world', int(mh * float(world_ob.split_ratio)))
                except Exception:
                    pass
                try:
                    if hasattr(world_ob, 'controller') and world_ob.controller:
                        world_ob.controller.update_on_camera_change()
                except Exception:
                    pass
        # One-time MOTION sample
        if not getattr(ctx.controller.events, '_split_drag_first_logged', False):
            try:
                src = 'visuals' if (ip is not None and hasattr(ip, 'visuals') and ip.visuals._find_building_entity_by_id(int(bid)) is ob) else 'world'
            except Exception:
                src = 'unknown'
            try:
                bx, by = cam.apply((getattr(ob, 'x', 0), getattr(ob, 'y', 0)))
                _, h_scaled = cam.scale(ob.image.get_size())
            except Exception:
                bx = by = h_scaled = None
            try:
                mx, my = event.pos
            except Exception:
                mx = my = None
            try:
                ratio_now = float(getattr(ob, 'split_ratio', 0.0))
            except Exception:
                ratio_now = None
            log_info_safe(
                ctx.logger,
                "SpawnerEditor: split drag MOTION1 bid=%s src=%s bx=%s by=%s h_scaled=%s mouse=(%s,%s) ratio=%s",
                str(bid), src, str(bx), str(by), str(h_scaled), str(mx), str(my),
                f"{ratio_now:.3f}" if isinstance(ratio_now, (int, float)) else str(ratio_now),
            )
            setattr(ctx.controller.events, '_split_drag_first_logged', True)
        # Propagate live ratio
        try:
            cur_r = float(getattr(ob, 'split_ratio', 0.5))
            propagate_split_ratio(ctx, ob, cur_r, ip)
        except Exception:
            pass
        return True
    except Exception:
        return False


def end_split_drag(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    model = ctx.model
    bid = getattr(model, 'split_drag_bid', None)
    model.split_drag_active = False
    model.split_drag_bid = None
    # Stop tool
    try:
        if ctx.split_tool is not None:
            ctx.split_tool.stop_drag()
        if ctx.split_adapter is not None:
            ctx.split_adapter.split_dragging = False
            ctx.split_adapter.selected_building = None
    except Exception:
        pass
    # Re-enable gameplay input
    try:
        if ctx.world is not None and hasattr(ctx.world, 'state'):
            setattr(ctx.world.state, 'spawner_input_suppressed', False)
    except Exception:
        pass
    if bid is not None:
        try:
            ip = getattr(ctx.controller, 'instance_properties', None)
            ob = None
            try:
                if ip is not None and hasattr(ip, 'visuals'):
                    ob = ip.visuals._find_building_entity_by_id(int(bid))
            except Exception:
                ob = None
            if ob is None:
                ob = find_building_in_world_by_id(ctx.world, int(bid))
            cur_ratio = float(getattr(ob, 'split_ratio', 0.5)) if ob is not None else None
        except Exception:
            cur_ratio = None
        # Log end
        log_info_safe(ctx.logger, "SpawnerEditor: split drag END bid=%s button=%s ratio=%s", str(bid), str(getattr(event, 'button', None)), f"{cur_ratio:.3f}" if isinstance(cur_ratio, (int, float)) else str(cur_ratio))
        if cur_ratio is not None:
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
                ov = e.get('overrides') or {}
                if not isinstance(ov, dict):
                    ov = {}
                ov['split_ratio'] = round(float(cur_ratio), 3)
                e['overrides'] = ov
                changed = True
                break
            if changed:
                try:
                    svc_write_buildings_instances(data)
                except Exception:
                    pass
            # Propagate end-state
            try:
                propagate_split_ratio(ctx, ob, float(cur_ratio), ip)
            except Exception:
                pass
    # Reset flags
    try:
        setattr(ctx.controller.events, '_split_drag_first_logged', False)
    except Exception:
        pass
    try:
        setattr(model, '_split_propagation_key', None)
    except Exception:
        pass
    return True
