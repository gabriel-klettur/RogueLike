from __future__ import annotations

"""Dibujo de overlays sobre edificios vinculados a spawners (hover/selección).

Incluye:
- Rectángulos de hover (cian) y selección (amarillo).
- Controles de Delete/Reset/Resize con feedback de hover.
- Paneles Z (top/bottom) y barra de split reutilizando vistas del editor de edificios.
"""

import pygame
from roguelike_ui.ui_blocker import is_blocked


def _draw_building_rect(screen: pygame.Surface, cam, ob, color, width: int) -> pygame.Rect | None:
    try:
        img = getattr(ob, 'image', getattr(getattr(ob, 'model', ob), 'image', None))
        x = getattr(ob, 'x', getattr(getattr(ob, 'model', ob), 'x', None))
        y = getattr(ob, 'y', getattr(getattr(ob, 'model', ob), 'y', None))
        if img is None or x is None or y is None:
            return None
        sx, sy = cam.apply((x, y))
        sw, sh = cam.scale(img.get_size())
        rect = pygame.Rect(int(sx), int(sy), int(sw), int(sh))
        pygame.draw.rect(screen, color, rect, width)
        return rect
    except Exception:
        return None


def render_buildings_overlays(view, screen: pygame.Surface) -> None:
    c = view.controller
    ip = getattr(c, 'instance_properties', None)
    cam = getattr(getattr(c, 'game', None), 'camera', None)
    if ip is None or cam is None:
        return

    vmodel = getattr(ip, 'visuals', None)
    vmodel = getattr(vmodel, 'model', None)
    sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
    hov_bid = getattr(vmodel, 'hovered_building_id', None) if vmodel else None

    # Per-frame fallback hover detection
    try:
        mx, my = pygame.mouse.get_pos()
        ob_hover = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
        if ob_hover is not None and getattr(ob_hover, 'id', None) is not None:
            hov_bid = int(getattr(ob_hover, 'id'))
    except Exception:
        pass

    target_bid = sel_bid if sel_bid is not None else hov_bid

    # Hover (cian)
    if hov_bid is not None and hov_bid != sel_bid:
        ob_h = None
        try:
            ob_h = ip.visuals._find_building_entity_by_id(int(hov_bid))
            if ob_h is None:
                ip.visuals._ensure_building_loaded(int(hov_bid))
                ob_h = ip.visuals._find_building_entity_by_id(int(hov_bid))
        except Exception:
            ob_h = None
        if ob_h is not None:
            _draw_building_rect(screen, cam, ob_h, (0, 255, 255), 2)

    # Selección (amarillo) + controles + z-panels + split
    if sel_bid is not None:
        ob = None
        try:
            ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
            if ob is None:
                ip.visuals._ensure_building_loaded(int(sel_bid))
                ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
            if ob is None:
                ob = view._find_building_entity_by_id_world(int(sel_bid))
        except Exception:
            ob = None
        if ob is not None:
            rect = _draw_building_rect(screen, cam, ob, (255, 215, 0), 5)
            if rect is not None:
                # ID label
                try:
                    if view._id_font is not None:
                        label = f"ID {int(sel_bid)}"
                        text_surf = view._id_font.render(label, True, (255, 255, 255))
                        shadow_surf = view._id_font.render(label, True, (0, 0, 0))
                        lx = rect.left
                        ly = rect.top - text_surf.get_height() - 2
                        if ly < 0:
                            ly = rect.top + 2
                        screen.blit(shadow_surf, (lx + 1, ly + 1))
                        screen.blit(text_surf, (lx, ly))
                except Exception:
                    pass
                # Controles Delete/Reset/Resize
                try:
                    mouse_pos = pygame.mouse.get_pos()
                    blocked = bool(is_blocked(*mouse_pos))
                except Exception:
                    mouse_pos = (0, 0)
                    blocked = False
                sw, sh = rect.width, rect.height
                handle_size = max(15, min(65, int(sw * 0.10)))
                # Delete
                del_rect = pygame.Rect(rect.left + sw - 3 * handle_size, rect.top, handle_size, handle_size)
                try:
                    view._last_selected_delete_rect = del_rect.copy()
                except Exception:
                    view._last_selected_delete_rect = del_rect
                is_hover_del = (not blocked) and del_rect.collidepoint(mouse_pos)
                pygame.draw.rect(screen, (220, 40, 40), del_rect)
                pygame.draw.rect(screen, (0, 0, 0), del_rect, 2)
                if is_hover_del:
                    pygame.draw.rect(screen, (255, 255, 0), del_rect, 4)
                pygame.draw.line(screen, (255, 255, 255), del_rect.topleft, del_rect.bottomright, 3)
                pygame.draw.line(screen, (255, 255, 255), del_rect.topright, del_rect.bottomleft, 3)
                # Reset
                rst_rect = pygame.Rect(rect.left + sw - 2 * handle_size, rect.top, handle_size, handle_size)
                try:
                    view._last_selected_reset_rect = rst_rect.copy()
                except Exception:
                    view._last_selected_reset_rect = rst_rect
                is_hover_rst = (not blocked) and rst_rect.collidepoint(mouse_pos)
                pygame.draw.rect(screen, (255, 255, 255), rst_rect)
                pygame.draw.rect(screen, (0, 0, 0), rst_rect, 2)
                if is_hover_rst:
                    pygame.draw.rect(screen, (0, 255, 255), rst_rect, 4)
                try:
                    dfont = pygame.font.SysFont("arial", int(handle_size * 0.6), bold=True)
                    ds = dfont.render('D', True, (0, 0, 0))
                    screen.blit(ds, ds.get_rect(center=rst_rect.center))
                except Exception:
                    pass
                # Resize
                rz_rect = pygame.Rect(rect.left + sw - handle_size, rect.top, handle_size, handle_size)
                try:
                    view._last_selected_resize_rect = rz_rect.copy()
                except Exception:
                    view._last_selected_resize_rect = rz_rect
                is_hover_rz = (not blocked) and rz_rect.collidepoint(mouse_pos)
                pygame.draw.rect(screen, (80, 120, 255), rz_rect)
                pygame.draw.rect(screen, (0, 0, 0), rz_rect, 2)
                if is_hover_rz:
                    pygame.draw.rect(screen, (255, 0, 255), rz_rect, 4)
                try:
                    pygame.draw.ellipse(screen, (255, 255, 0), rz_rect, 5)
                    rfont = pygame.font.SysFont("arial", int(handle_size * 0.8), bold=True)
                    rs = rfont.render('R', True, (255, 255, 0))
                    screen.blit(rs, rs.get_rect(center=rz_rect.center))
                except Exception:
                    pass
                # Z toolbars
                try:
                    if view._z_bottom_view is not None:
                        zb = view._z_bottom_view.render(screen, ob, cam)
                        if isinstance(zb, dict):
                            px, py = zb.get('panel_pos', (0, 0))
                            m = zb.get('minus_rect')
                            p = zb.get('plus_rect')
                            if m is not None:
                                view._last_z_bottom_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                            if p is not None:
                                view._last_z_bottom_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                    if view._z_top_view is not None:
                        zt = view._z_top_view.render(screen, ob, cam)
                        if isinstance(zt, dict):
                            px, py = zt.get('panel_pos', (0, 0))
                            m = zt.get('minus_rect')
                            p = zt.get('plus_rect')
                            if m is not None:
                                view._last_z_top_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                            if p is not None:
                                view._last_z_top_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                except Exception:
                    pass
                # Split bar
                try:
                    if view._split_view is not None:
                        sret = view._split_view.render(screen, ob, cam)
                        if isinstance(sret, dict):
                            view._last_split_handle_rect = sret.get('handle_rect')
                except Exception:
                    pass

    # Sin selección: aún dibujar Z panels/split para hovered
    if sel_bid is None and target_bid is not None:
        ob_t = None
        try:
            ob_t = ip.visuals._find_building_entity_by_id(int(target_bid))
            if ob_t is None:
                ip.visuals._ensure_building_loaded(int(target_bid))
                ob_t = ip.visuals._find_building_entity_by_id(int(target_bid))
        except Exception:
            ob_t = None
        if ob_t is None:
            ob_t = view._find_building_entity_by_id_world(int(target_bid))
        if ob_t is not None:
            try:
                if view._z_bottom_view is not None:
                    zb = view._z_bottom_view.render(screen, ob_t, cam)
                    if isinstance(zb, dict):
                        px, py = zb.get('panel_pos', (0, 0))
                        m = zb.get('minus_rect')
                        p = zb.get('plus_rect')
                        if m is not None:
                            view._last_z_bottom_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                        if p is not None:
                            view._last_z_bottom_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                if view._z_top_view is not None:
                    zt = view._z_top_view.render(screen, ob_t, cam)
                    if isinstance(zt, dict):
                        px, py = zt.get('panel_pos', (0, 0))
                        m = zt.get('minus_rect')
                        p = zt.get('plus_rect')
                        if m is not None:
                            view._last_z_top_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                        if p is not None:
                            view._last_z_top_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                if view._split_view is not None:
                    sret = view._split_view.render(screen, ob_t, cam)
                    if isinstance(sret, dict):
                        view._last_split_handle_rect = sret.get('handle_rect')
            except Exception:
                pass
