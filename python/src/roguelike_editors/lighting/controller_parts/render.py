from __future__ import annotations

import pygame
from roguelike_editors.lighting.services.light_instances_service import (
    load_light_instances,
    _load_presets,
)
from .utils import (
    get_cam_params,
    compute_instance_world,
    world_to_screen,
    merge_params,
)


def render_instances_overlay(ctl, screen: pygame.Surface) -> None:
    try:
        if not bool(getattr(ctl.model, "overlay_visible", True)):
            return
        cam = getattr(ctl.game, "camera", None)
        if cam is None:
            return
        z, ox, oy = get_cam_params(cam)
        presets = _load_presets()
        insts = load_light_instances() or []
    except Exception:
        return

    sw, sh = screen.get_size()
    rect_screen = pygame.Rect(0, 0, sw, sh)
    try:
        font = getattr(ctl.view, "font", None) or pygame.font.Font(None, 14)
    except Exception:
        font = None
    try:
        mx, my = pygame.mouse.get_pos()
    except Exception:
        mx = my = -9999
    show_labels = bool(getattr(ctl.model, "overlay_labels", True))
    hovered_preset: str | None = None

    for e in insts:
        try:
            zone = str(e.get("zone") or "no zone")
            rel_x = int(e.get("rel_x") or 0)
            rel_y = int(e.get("rel_y") or 0)
            wx, wy = compute_instance_world(zone, rel_x, rel_y)
            sx, sy = world_to_screen(wx, wy, z, ox, oy)
            preset_id = str(e.get("preset_id") or "")
            params = merge_params(presets, preset_id, e.get("overrides") if isinstance(e, dict) else None)
            radius = int(params.get("radius", 160))
            pal = getattr(ctl.model, "overlay_palette", {}) or {}
            color = pal.get(preset_id, params.get("color", (255, 200, 140)))
            try:
                cr = int(color[0]); cg = int(color[1]); cb = int(color[2])
            except Exception:
                cr, cg, cb = 255, 200, 140
            rr = int(max(1, radius) * z)
            if rr <= 1:
                continue
            bb = pygame.Rect(sx - rr, sy - rr, rr * 2, rr * 2)
            if not rect_screen.colliderect(bb):
                continue
            try:
                lid_int = int(e.get("id")) if e.get("id") is not None else None
            except Exception:
                lid_int = None
            is_selected = lid_int is not None and lid_int == getattr(ctl.model, "selected_light_id", None)
            if is_selected and bool(getattr(ctl.model, "_dragging_inst", False)):
                wx_drag = getattr(ctl.model, "_drag_world_x", None)
                wy_drag = getattr(ctl.model, "_drag_world_y", None)
                if wx_drag is not None and wy_drag is not None:
                    sx = int((float(wx_drag) - ox) * z)
                    sy = int((float(wy_drag) - oy) * z)
                    bb = pygame.Rect(sx - rr, sy - rr, rr * 2, rr * 2)
                    if not rect_screen.colliderect(bb):
                        continue
            dx = mx - sx; dy = my - sy
            hovered = (dx * dx + dy * dy) <= (rr * rr)
            if hovered:
                hovered_preset = preset_id
            col = (80, 240, 255) if is_selected else ((255, 245, 120) if hovered else (cr, cg, cb))
            w_main = 3 if is_selected else (2 if hovered else 1)
            pygame.draw.circle(screen, col, (sx, sy), rr, width=w_main)
            pygame.draw.circle(screen, col, (sx, sy), max(1, int(2 * z)), width=1)
            if font is not None and show_labels:
                try:
                    lid = e.get("id")
                    label = f"#{int(lid)} {preset_id} (r={radius})" if lid is not None else f"{preset_id} (r={radius})"
                except Exception:
                    label = f"{preset_id} (r={radius})"
                ts = font.render(label, True, (10, 10, 14))
                tw, th = ts.get_width(), ts.get_height()
                lx = sx + rr + 6
                ly = sy - th // 2
                if lx + tw > sw - 4:
                    lx = max(4, sx - rr - 6 - tw)
                if ly + th > sh - 4:
                    ly = max(4, sh - th - 4)
                bg = pygame.Surface((tw + 6, th + 4), pygame.SRCALPHA)
                bg.fill((245, 245, 250, 220))
                screen.blit(bg, (lx - 3, ly - 2))
                screen.blit(ts, (lx, ly))
        except Exception:
            continue

    try:
        ctl.model._hovered_preset_id = hovered_preset
    except Exception:
        pass
