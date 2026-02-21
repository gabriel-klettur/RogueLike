from __future__ import annotations

import pygame
import importlib
from .actions import spawn_at_screen
from .utils import (
    get_cam_params,
    compute_instance_world,
    world_to_screen,
    screen_to_world,
    merge_params,
    get_presets,
    get_light_instances,
)


def on_mousebuttondown(ctl, event: pygame.event.Event) -> bool:
    """Handle left button down: panel delegation, spawn placement, selection/drag start."""
    if getattr(event, "button", None) != 1:
        return False

    st = ctl.model
    pan_left = getattr(st, "_panel_rect", None)
    pan_day = getattr(ctl.daytime_state, "panel_rect", None)
    pan_preset = getattr(ctl.presets_state, "panel_rect", None)

    # Delegate to panels
    if isinstance(pan_day, pygame.Rect) and pan_day.collidepoint(event.pos):
        try:
            from ..panels.day_time_panel.day_time_panel_events import DayTimePanelEventHandler
            DayTimePanelEventHandler.handle_event(ctl.daytime_controller, event)
        except Exception:
            pass
        return True
    if isinstance(pan_preset, pygame.Rect) and pan_preset.collidepoint(event.pos):
        try:
            from ..panels.light_presets_panel.light_presets_panel_events import LightPresetsPanelEventHandler
            LightPresetsPanelEventHandler.handle_event(ctl.presets_controller, event)
        except Exception:
            pass
        return True

    outside_left = not (isinstance(pan_left, pygame.Rect) and pan_left.collidepoint(event.pos))
    outside_day = not (isinstance(pan_day, pygame.Rect) and pan_day.collidepoint(event.pos))
    outside_preset = not (isinstance(pan_preset, pygame.Rect) and pan_preset.collidepoint(event.pos))

    # Spawn mode: place light on map
    if getattr(st, "spawn_mode", False) and outside_left and outside_day and outside_preset:
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            get_global_lighting().set_enabled(True)
        except Exception:
            pass
        spawn_at_screen(ctl, event.pos)
        try:
            if bool(getattr(ctl.presets_state, "spawn_single_shot", False)):
                st.spawn_mode = False
                setattr(ctl.presets_state, "spawn_mode", False)
        except Exception:
            pass
        return True

    # Selection/drag start when overlay is visible and clicking on a light outline
    if bool(getattr(st, "overlay_visible", True)) and outside_left and outside_day and outside_preset:
        mx, my = int(event.pos[0]), int(event.pos[1])
        cam = getattr(ctl.game, "camera", None)
        if cam is not None:
            z, ox, oy = get_cam_params(cam)
            hit_id = None
            try:
                ctrl_mod = importlib.import_module("roguelike_editors.lighting.lighting_controller")
            except Exception:
                ctrl_mod = None
            if ctrl_mod is not None and hasattr(ctrl_mod, "_load_presets"):
                try:
                    presets = dict(getattr(ctrl_mod, "_load_presets")())  # type: ignore
                except Exception:
                    presets = get_presets()
            else:
                presets = get_presets()

            if ctrl_mod is not None and hasattr(ctrl_mod, "load_light_instances"):
                try:
                    instances = list(getattr(ctrl_mod, "load_light_instances")())  # type: ignore
                except Exception:
                    instances = get_light_instances()
            else:
                instances = get_light_instances()

            for e in instances:
                try:
                    zone = str(e.get("zone") or "no zone")
                    rel_x = int(e.get("rel_x") or 0)
                    rel_y = int(e.get("rel_y") or 0)
                    wx, wy = compute_instance_world(zone, rel_x, rel_y)
                    sx, sy = world_to_screen(wx, wy, z, ox, oy)
                    preset_id = str(e.get("preset_id") or "")
                    params = merge_params(presets, preset_id, e.get("overrides") if isinstance(e, dict) else None)
                    radius = int(params.get("radius", 160))
                    rr = int(max(1, radius) * z)
                    dx, dy = mx - sx, my - sy
                    if dx * dx + dy * dy <= rr * rr:
                        hit_id = int(e.get("id")) if e.get("id") is not None else None
                        break
                except Exception:
                    continue
            # Fallback: pick nearest within small threshold if none matched (robustness)
            if hit_id is None:
                best = (10_000_000, None)
                for e in get_light_instances():
                    try:
                        zone = str(e.get("zone") or "no zone")
                        rel_x = int(e.get("rel_x") or 0)
                        rel_y = int(e.get("rel_y") or 0)
                        wx, wy = compute_instance_world(zone, rel_x, rel_y)
                        sx, sy = world_to_screen(wx, wy, z, ox, oy)
                        dx, dy = mx - sx, my - sy
                        d2 = dx * dx + dy * dy
                        if d2 < best[0]:
                            best = (d2, int(e.get("id")) if e.get("id") is not None else None)
                    except Exception:
                        continue
                # Accept if within 4px radius
                if best[1] is not None and best[0] <= 16:
                    hit_id = best[1]
            if hit_id is not None:
                try:
                    mods = getattr(event, "mod", 0) or pygame.key.get_mods()
                    ctrl = bool(mods & pygame.KMOD_CTRL)
                except Exception:
                    ctrl = False
                try:
                    sel_set = getattr(st, "selected_light_ids", set())
                except Exception:
                    sel_set = set()
                if ctrl:
                    if hit_id in sel_set:
                        try:
                            sel_set.remove(hit_id)
                        except Exception:
                            pass
                    else:
                        sel_set.add(hit_id)
                    st.selected_light_ids = sel_set
                    st.selected_light_id = hit_id
                    return True
                else:
                    st.selected_light_ids = {hit_id}
                    st.selected_light_id = hit_id
                    st._dragging_inst = True
                    wx, wy = screen_to_world(mx, my, cam)
                    st._drag_world_x = wx
                    st._drag_world_y = wy
                    return True

    return False


def on_mousemotion_drag_instance(ctl, event: pygame.event.Event) -> bool:
    if event.type != pygame.MOUSEMOTION or not bool(getattr(ctl.model, "_dragging_inst", False)):
        return False
    try:
        cam = getattr(ctl.game, "camera", None)
        if cam is None:
            return False
        mx, my = int(getattr(event, "pos", (0, 0))[0]), int(getattr(event, "pos", (0, 0))[1])
        wx, wy = screen_to_world(mx, my, cam)
        ctl.model._drag_world_x = wx
        ctl.model._drag_world_y = wy
        lid = getattr(ctl.model, "selected_light_id", None)
        if lid is not None:
            try:
                from roguelike_engine.rendering.lighting import get_global_lighting
                lm = get_global_lighting()
                pid = f"persist:{int(lid)}"
                for lt in getattr(lm, "_lights", []):
                    try:
                        if getattr(lt, "id", None) == pid:
                            lt.x = float(ctl.model._drag_world_x)
                            lt.y = float(ctl.model._drag_world_y)
                            break
                    except Exception:
                        continue
            except Exception:
                pass
    except Exception:
        pass
    return True


def on_mousebuttonup_stopdrag(ctl, event: pygame.event.Event) -> bool:
    if event.type != pygame.MOUSEBUTTONUP or not getattr(ctl.model, "_dragging_inst", False):
        return False
    st = ctl.model
    st._dragging_inst = False
    lid = getattr(st, "selected_light_id", None)
    wx = getattr(st, "_drag_world_x", None)
    wy = getattr(st, "_drag_world_y", None)
    if lid is not None and wx is not None and wy is not None:
        try:
            try:
                ctrl_mod = importlib.import_module("roguelike_editors.lighting.lighting_controller")
            except Exception:
                ctrl_mod = None
            if ctrl_mod is not None and hasattr(ctrl_mod, "update_instance_position"):
                ctrl_mod.update_instance_position(int(lid), float(wx), float(wy))  # type: ignore
            else:
                from roguelike_editors.lighting.services.light_instances_service import update_instance_position as _svc_uip  # type: ignore
                _svc_uip(int(lid), float(wx), float(wy))
        except Exception:
            pass
        try:
            from roguelike_engine.rendering.lighting import get_global_lighting
            lm = get_global_lighting()
            pid = f"persist:{int(lid)}"
            for lt in getattr(lm, "_lights", []):
                try:
                    if getattr(lt, "id", None) == pid:
                        lt.x = float(wx)
                        lt.y = float(wy)
                        break
                except Exception:
                    continue
        except Exception:
            pass
    return True
