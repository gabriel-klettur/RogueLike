from __future__ import annotations

import pygame
from .core import draw_overlay, draw_shadow, draw_panel, draw_scrollbar
from .utils import get_surface


def draw_saves_panel(
    renderer,
    screen: pygame.Surface,
    selected: int,
    items: list[str],
    detail_lines: list[str],
    *,
    row_scroll_offset: int = 0,
    hovered_index: int | None = None,
    fixed_panel_size: tuple[int, int] | None = None,
    fixed_list_width: int | None = None,
    fixed_details_width: int | None = None,
    hover_details_name: bool = False,
    editing_name: bool = False,
    edit_name_text: str | None = None,
    caret_pos: int = 0,
    hover_load_button: bool = False,
    hover_delete_button: bool = False,
    select_all_edit: bool = False,
    panel_top_min: int | None = None,
) -> pygame.Rect:
    overlay_rect = draw_overlay(renderer, screen)

    if fixed_list_width is None:
        list_max_w = 0
        for label in items:
            tw, _ = renderer.font.size(label)
            list_max_w = max(list_max_w, tw)
    else:
        list_max_w = int(fixed_list_width)

    if fixed_details_width is None:
        details_max_w = 0
        for line in detail_lines:
            tw, _ = renderer.font.size(line)
            details_max_w = max(details_max_w, tw)
    else:
        details_max_w = int(fixed_details_width)

    col_gap = 32
    n_items = len(items)

    if fixed_panel_size is None:
        w = renderer.padding_x * 2 + list_max_w + col_gap + details_max_w + 12
        inner_rows_h = (n_items or 1) * renderer.line_height + max(0, (n_items - 1)) * renderer.item_gap
        h = renderer.padding_y * 2 + max(inner_rows_h, renderer.line_height * 5)
        sw, sh = screen.get_size()
        w = min(w, int(sw * 0.95))
        h = min(h, int(sh * 0.85))
    else:
        w, h = fixed_panel_size

    panel_rect = pygame.Rect(0, 0, w, h)
    panel_rect = panel_rect.move(((screen.get_width() - w) // 2, (screen.get_height() - h) // 2))
    if isinstance(panel_top_min, int):
        sw, sh = screen.get_size()
        extra = max(24, int(renderer.line_height)) + 100
        desired_top = panel_top_min + extra
        bottom_margin = 12
        max_h_available = max(60, (sh - bottom_margin) - desired_top)
        if h > max_h_available:
            h = max_h_available
            panel_rect.height = h
        panel_rect.top = desired_top

    draw_shadow(renderer, screen, panel_rect)
    panel = draw_panel(renderer, (w, h))

    renderer.last_saves_layout = {
        'panel_rect': panel_rect,
        'row_rects': {},
        'start': 0,
        'end': 0,
        'details_name_rect': None,
        'load_button_rect': None,
        'delete_button_rect': None,
    }

    list_x = renderer.padding_x
    list_y = renderer.padding_y
    inner_height = h - renderer.padding_y * 2
    block_h = renderer.line_height + renderer.item_gap
    max_visible = max(1, (inner_height + renderer.item_gap) // block_h)

    if n_items <= max_visible:
        start = 0
        end = n_items
        row_scroll_offset = 0
    else:
        max_offset = max(0, n_items - max_visible)
        row_scroll_offset = max(0, min(row_scroll_offset, max_offset))
        start = row_scroll_offset
        end = start + max_visible

    y = list_y
    for i in range(start, end):
        label = items[i]
        is_sel = (i == selected)
        if is_sel:
            pill_rect = pygame.Rect(list_x - 2, y, min(list_max_w + 16, w // 2), renderer.line_height)
            pygame.draw.rect(panel, renderer.highlight_color, pill_rect, border_radius=renderer.radius // 2)
            accent_rect = pygame.Rect(list_x - 8, y, 4, renderer.line_height)
            pygame.draw.rect(panel, renderer.accent_color, accent_rect, border_radius=2)

        color = renderer.accent_color if is_sel else renderer.text_color
        text = renderer.font.render(label, True, color)
        ty = y + (renderer.line_height - text.get_height()) // 2
        panel.blit(text, (list_x + 8, ty))
        row_rect = pygame.Rect(list_x - 4, y - 2, list_max_w + 24, renderer.line_height + 4)
        if hovered_index == i and not is_sel:
            pygame.draw.rect(panel, renderer.border_color, row_rect, width=2, border_radius=6)
        renderer.last_saves_layout['row_rects'][i] = row_rect.move(panel_rect.topleft)
        y += block_h

    if n_items > max_visible:
        track_rect = pygame.Rect(w - renderer.padding_x // 2 - 6, renderer.padding_y, 6, inner_height)
        draw_scrollbar(renderer, panel, track_rect, max_visible=max_visible, total=n_items, start_index=start)

    details_x = renderer.padding_x + (fixed_list_width or list_max_w) + col_gap
    details_y = renderer.padding_y
    for i, line in enumerate(detail_lines):
        ty = details_y
        if i == 0:
            prefix = "Nombre: "
            value = ""
            if editing_name:
                value = edit_name_text or ""
            else:
                if line.startswith(prefix):
                    value = line[len(prefix):]
                else:
                    prefix = ""
                    value = line
            pt = renderer.font.render(prefix, True, renderer.text_color)
            panel.blit(pt, (details_x, ty + (renderer.line_height - pt.get_height()) // 2))
            px = details_x + pt.get_width()
            vt = renderer.font.render(value if value else " ", True, renderer.text_color)
            vy = ty + (renderer.line_height - vt.get_height()) // 2
            panel.blit(vt, (px, vy))
            name_rect = pygame.Rect(px - 4, ty - 2, max(vt.get_width(), 80) + 8, renderer.line_height + 4)
            if editing_name and select_all_edit:
                sel_bg = (255, 220, 0, 48)
                pygame.draw.rect(panel, sel_bg, name_rect, border_radius=6)
            if hover_details_name or editing_name:
                pygame.draw.rect(panel, renderer.border_color, name_rect, width=2, border_radius=6)
            if editing_name:
                cpos = max(0, min(caret_pos, len(value)))
                caret_text = value[:cpos]
                cw, _ = renderer.font.size(caret_text if caret_text else "")
                cx = px + cw
                cy = ty + 4
                ch = renderer.line_height - 8
                pygame.draw.rect(panel, renderer.accent_color, pygame.Rect(cx, cy, 2, ch), border_radius=1)
            renderer.last_saves_layout['details_name_rect'] = name_rect.move(panel_rect.topleft)
        else:
            t = renderer.font.render(line, True, renderer.text_color)
            panel.blit(t, (details_x, ty + (renderer.line_height - t.get_height()) // 2))
        details_y += renderer.line_height + (renderer.item_gap - 2)

    renderer.last_saves_layout['start'] = start
    renderer.last_saves_layout['end'] = end
    renderer.last_saves_layout['scroll_offset'] = row_scroll_offset

    load_label = "Cargar"
    del_label = "Borrar"
    bt_load = renderer.font.render(load_label, True, renderer.text_color)
    bt_del = renderer.font.render(del_label, True, renderer.text_color)
    btn_pad_x = 18
    btn_h = renderer.line_height
    load_w = bt_load.get_width() + btn_pad_x * 2
    del_w = bt_del.get_width() + btn_pad_x * 2
    gap = 16
    total_w = load_w + gap + del_w
    base_y = h - renderer.padding_y - btn_h
    base_x = (w - total_w) // 2

    del_rect_local = pygame.Rect(base_x, base_y, del_w, btn_h)
    load_rect_local = pygame.Rect(base_x + del_w + gap, base_y, load_w, btn_h)

    btn_bg = (255, 255, 255, 22)
    pygame.draw.rect(panel, btn_bg, del_rect_local, border_radius=renderer.radius // 2)
    if hover_delete_button:
        pygame.draw.rect(panel, renderer.border_color, del_rect_local, width=2, border_radius=renderer.radius // 2)
    dx = del_rect_local.x + (del_rect_local.width - bt_del.get_width()) // 2
    dy = del_rect_local.y + (del_rect_local.height - bt_del.get_height()) // 2
    panel.blit(bt_del, (dx, dy))

    pygame.draw.rect(panel, btn_bg, load_rect_local, border_radius=renderer.radius // 2)
    if hover_load_button:
        pygame.draw.rect(panel, renderer.border_color, load_rect_local, width=2, border_radius=renderer.radius // 2)
    lx = load_rect_local.x + (load_rect_local.width - bt_load.get_width()) // 2
    ly = load_rect_local.y + (load_rect_local.height - bt_load.get_height()) // 2
    panel.blit(bt_load, (lx, ly))

    renderer.last_saves_layout['load_button_rect'] = load_rect_local.move(panel_rect.topleft)
    renderer.last_saves_layout['delete_button_rect'] = del_rect_local.move(panel_rect.topleft)

    screen.blit(get_surface(panel), panel_rect.topleft)
    return overlay_rect


def draw_saves(renderer, screen: pygame.Surface, selected: int, items: list[str], detail_lines: list[str]) -> pygame.Rect:
    overlay_rect = draw_overlay(renderer, screen)

    list_max_w = 0
    for label in items:
        tw, _ = renderer.font.size(label)
        list_max_w = max(list_max_w, tw)
    details_max_w = 0
    for line in detail_lines:
        tw, _ = renderer.font.size(line)
        details_max_w = max(details_max_w, tw)

    col_gap = 32
    w = renderer.padding_x * 2 + list_max_w + col_gap + details_max_w + 12

    list_rows_h = (len(items) or 1) * renderer.line_height + max(0, (len(items) - 1)) * renderer.item_gap
    details_rows_h = (len(detail_lines) or 1) * renderer.line_height + max(0, (len(detail_lines) - 1)) * (renderer.item_gap - 2)
    inner_h = max(list_rows_h, details_rows_h)
    h = renderer.padding_y * 2 + inner_h

    sw, sh = screen.get_size()
    max_w = min(w, int(sw * 0.9))
    max_h = min(h, int(sh * 0.85))
    w, h = max_w, max_h

    panel_rect = pygame.Rect((sw - w) // 2, (sh - h) // 2, w, h)

    draw_shadow(renderer, screen, panel_rect)
    panel = draw_panel(renderer, (w, h))

    renderer.last_blits = []
    list_x = renderer.padding_x
    list_y = renderer.padding_y
    for i, label in enumerate(items):
        is_sel = (i == selected)
        if is_sel:
            pill_rect = pygame.Rect(list_x - 2, list_y, list_max_w + 16, renderer.line_height)
            pygame.draw.rect(panel, renderer.highlight_color, pill_rect, border_radius=renderer.radius // 2)
            accent_rect = pygame.Rect(list_x - 8, list_y, 4, renderer.line_height)
            pygame.draw.rect(panel, renderer.accent_color, accent_rect, border_radius=2)
        color = renderer.accent_color if is_sel else renderer.text_color
        text = renderer.font.render(label, True, color)
        ty = list_y + (renderer.line_height - text.get_height()) // 2
        panel.blit(text, (list_x + 8, ty))
        renderer.last_blits.append((list_x + 8, ty))
        list_y += renderer.line_height + renderer.item_gap

    details_x = renderer.padding_x + list_max_w + col_gap
    details_y = renderer.padding_y
    for line in detail_lines:
        t = renderer.font.render(line, True, renderer.text_color)
        ty = details_y + (renderer.line_height - t.get_height()) // 2
        panel.blit(t, (details_x, ty))
        details_y += renderer.line_height + (renderer.item_gap - 2)

    screen.blit(get_surface(panel), panel_rect.topleft)
    return overlay_rect
