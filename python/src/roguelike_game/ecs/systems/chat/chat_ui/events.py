from __future__ import annotations

import pygame


def handle_chat_ui_events(world, events) -> None:
    """Handle UI interactions for the chat panel (scroll, resize, scrollbar dragging).

    Extracted from the original chat_ui_system for clarity and testability.
    """
    state = getattr(world, 'state', None)
    if not state or not getattr(state, 'chat_open', False):
        return
    panel_rect = getattr(state, 'chat_block_rect', None)
    if not panel_rect:
        return
    # Defaults
    if not hasattr(state, 'chat_scroll_lines'):
        state.chat_scroll_lines = 0
    # Drag de resize
    resizing = bool(getattr(state, 'chat_resizing', False))
    resize_rect = getattr(state, 'chat_resize_rect', None)
    sb_rect = getattr(state, 'chat_scrollbar_rect', None)
    thumb_rect = getattr(state, 'chat_scrollbar_thumb_rect', None)
    dragging_thumb = bool(getattr(state, 'chat_dragging_thumb', False))

    for ev in events:
        if ev.type == pygame.MOUSEWHEEL:
            mx, my = pygame.mouse.get_pos()
            if panel_rect.collidepoint(mx, my):
                # rueda hacia arriba: ev.y > 0
                step = 3
                state.chat_scroll_lines = max(0, int(state.chat_scroll_lines) + (step if ev.y > 0 else -step))
        elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            mx, my = ev.pos
            if resize_rect and resize_rect.collidepoint(mx, my):
                state.chat_resizing = True
                state.chat_resize_start = (mx, my)
                state.chat_resize_wh0 = (
                    int(getattr(state, 'chat_panel_w', 400) or 400),
                    int(getattr(state, 'chat_panel_h', 200) or 200),
                )
            elif thumb_rect and thumb_rect.collidepoint(mx, my):
                state.chat_dragging_thumb = True
                state.chat_drag_thumb_off = my - thumb_rect.y
            elif sb_rect and sb_rect.collidepoint(mx, my):
                # click en el track: posicionar
                thumb_h = thumb_rect.h if thumb_rect else 30
                rel = my - sb_rect.y - thumb_h // 2
                rel = max(0, min(rel, sb_rect.h - thumb_h))
                # Convertir a scroll
                total = int(getattr(state, 'chat_total_lines', 0) or 0)
                vis = int(getattr(state, 'chat_visible_lines', 1) or 1)
                max_scroll = max(0, total - vis)
                if sb_rect.h - thumb_h > 0:
                    pos_frac = rel / float(sb_rect.h - thumb_h)
                else:
                    pos_frac = 0.0
                # Invertido: pos_frac=1 (abajo) => scroll=0 (últimos)
                state.chat_scroll_lines = int(round(max_scroll * (1.0 - pos_frac)))
        elif ev.type == pygame.MOUSEBUTTONUP and ev.button == 1:
            state.chat_resizing = False
            state.chat_dragging_thumb = False
        elif ev.type == pygame.MOUSEMOTION:
            mx, my = ev.pos
            if dragging_thumb:
                # ajustar segun posición
                off = int(getattr(state, 'chat_drag_thumb_off', 0) or 0)
                if sb_rect and thumb_rect:
                    thumb_h = thumb_rect.h
                    rel = my - sb_rect.y - off
                    rel = max(0, min(rel, sb_rect.h - thumb_h))
                    total = int(getattr(state, 'chat_total_lines', 0) or 0)
                    vis = int(getattr(state, 'chat_visible_lines', 1) or 1)
                    max_scroll = max(0, total - vis)
                    if sb_rect.h - thumb_h > 0:
                        pos_frac = rel / float(sb_rect.h - thumb_h)
                    else:
                        pos_frac = 0.0
                    # Invertido: pos_frac=1 (abajo) => scroll=0 (últimos)
                    state.chat_scroll_lines = int(round(max_scroll * (1.0 - pos_frac)))
            if resizing:
                sx, sy = getattr(state, 'chat_resize_start', (mx, my))
                w0, h0 = getattr(state, 'chat_resize_wh0', (400, 200))
                dx = mx - sx
                dy = my - sy
                # Esquina superior derecha: dx aumenta ancho, dy hacia abajo reduce alto
                new_w = max(320, min(1200, int(w0 + dx)))
                new_h = max(160, min(600, int(h0 - dy)))
                state.chat_panel_w = new_w
                state.chat_panel_h = new_h
