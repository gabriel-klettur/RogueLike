from __future__ import annotations

import pygame
from .core import draw_overlay, draw_shadow, draw_panel, draw_scrollbar
from .utils import get_surface


def draw_table_with_tabs(
    renderer,
    screen: pygame.Surface,
    tabs: list[str],
    active_tab_index: int,
    headers: list[str],
    rows: list[list[str]],
    selected_row: int = 0,
    selected_col: int = 0,
    row_scroll_offset: int = 0,
    hovered_row: int | None = None,
    hovered_col: int | None = None,
    fixed_size: tuple[int, int] | None = None,
    fixed_col_widths: list[int] | None = None,
    panel_top_min: int | None = None,
) -> pygame.Rect:
    overlay_rect = draw_overlay(renderer, screen)

    ncols = len(headers)
    col_gap = max(20, renderer.padding_x - 8)
    if fixed_col_widths is not None and len(fixed_col_widths) >= ncols:
        col_widths = list(fixed_col_widths[:ncols])
    else:
        col_widths = [0] * max(1, ncols)
        for i, htxt in enumerate(headers):
            tw, _ = renderer.font.size(htxt)
            col_widths[i] = max(col_widths[i], tw)
        for row in rows:
            for i, cell in enumerate(row[:ncols]):
                tw, _ = renderer.font.size(cell)
                col_widths[i] = max(col_widths[i], tw)
    inner_w = sum(col_widths) + col_gap * max(0, (ncols - 1))

    tab_pad_x = 14
    tab_gap = 10
    tab_label_ws = [renderer.font.size(t)[0] for t in tabs]
    tabs_w = sum((w + tab_pad_x * 2) for w in tab_label_ws) + tab_gap * max(0, len(tabs) - 1)
    tabs_h = renderer.line_height

    w = renderer.padding_x * 2 + max(inner_w, tabs_w)
    total_rows = len(rows)
    header_h = renderer.line_height
    rows_h = (total_rows or 1) * renderer.line_height + max(0, (total_rows - 1)) * renderer.item_gap
    h = (renderer.padding_y * 2 + tabs_h + renderer.item_gap // 2 + header_h + renderer.item_gap + rows_h)

    sw, sh = screen.get_size()
    if fixed_size is not None:
        fw, fh = fixed_size
        w = min(fw, int(sw * 0.95))
        h = min(fh, int(sh * 0.85))
    else:
        w = min(w, int(sw * 0.95))
        h = min(h, int(sh * 0.85))
    panel_rect = pygame.Rect((sw - w) // 2, (sh - h) // 2, w, h)
    if isinstance(panel_top_min, int) and panel_rect.top < panel_top_min:
        bottom_margin = 12
        max_h_available = max(60, (sh - bottom_margin) - panel_top_min)
        if h > max_h_available:
            h = max_h_available
            panel_rect.height = h
        panel_rect.top = panel_top_min

    draw_shadow(renderer, screen, panel_rect)
    panel = draw_panel(renderer, (w, h))

    renderer.last_blits = []
    tabs_x = renderer.padding_x
    tabs_y = renderer.padding_y
    tab_rects: list[pygame.Rect] = []
    cx = tabs_x
    for i, label in enumerate(tabs):
        lw = tab_label_ws[i]
        tw = lw + tab_pad_x * 2
        rect = pygame.Rect(cx, tabs_y, tw, tabs_h)
        is_active = (i == active_tab_index)
        bg_col = (50, 52, 58, 160) if not is_active else (255, 200, 0, 38)
        pygame.draw.rect(panel, bg_col, rect, border_radius=10)
        if is_active:
            pygame.draw.rect(panel, renderer.border_color, rect, width=2, border_radius=10)
        color = renderer.accent_color if is_active else renderer.text_color
        t = renderer.font.render(label, True, color)
        ty = rect.y + (rect.height - t.get_height()) // 2
        tx = rect.x + (rect.width - t.get_width()) // 2
        panel.blit(t, (tx, ty))
        tab_rects.append(rect.move(panel_rect.topleft))
        cx += tw + tab_gap

    x = renderer.padding_x
    y = renderer.padding_y + tabs_h + renderer.item_gap // 2
    for i, htxt in enumerate(headers):
        t = renderer.font.render(htxt, True, renderer.text_color_dim)
        ty = y + (renderer.line_height - t.get_height()) // 2
        panel.blit(t, (x, ty))
        renderer.last_blits.append((x, ty))
        x += col_widths[i] + col_gap
    sep_y = y + header_h + (renderer.item_gap // 2)
    pygame.draw.line(panel, (255, 255, 255, 35), (renderer.padding_x, sep_y), (w - renderer.padding_x, sep_y), 1)

    inner_height = h - (renderer.padding_y * 2 + tabs_h + renderer.item_gap // 2 + header_h + renderer.item_gap)
    block_h = renderer.line_height + renderer.item_gap
    max_visible = max(1, (inner_height + renderer.item_gap) // block_h)
    if total_rows <= max_visible:
        start = 0
        end = total_rows
    else:
        max_offset = max(0, total_rows - max_visible)
        row_scroll_offset = max(0, min(row_scroll_offset, max_offset))
        start = row_scroll_offset
        end = start + max_visible

    renderer.last_table_layout = {
        'panel_rect': panel_rect,
        'start_row': 0,
        'end_row': 0,
        'cell_rects': {},
        'tab_rects': tab_rects,
    }

    y = renderer.padding_y + tabs_h + renderer.item_gap // 2 + header_h + renderer.item_gap
    for r in range(start, end):
        cells = rows[r]
        is_sel_row = (r == selected_row)
        if is_sel_row:
            pill_rect = pygame.Rect(renderer.padding_x, y, w - renderer.padding_x * 2, renderer.line_height)
            pygame.draw.rect(panel, renderer.highlight_color, pill_rect, border_radius=renderer.radius // 2)
            accent_rect = pygame.Rect(renderer.padding_x - 6, y, 4, renderer.line_height)
            pygame.draw.rect(panel, renderer.accent_color, accent_rect, border_radius=2)

        cx = renderer.padding_x
        for c in range(ncols):
            text_val = cells[c] if c < len(cells) else ""
            color = renderer.accent_color if (is_sel_row and c == selected_col) else (renderer.accent_color if is_sel_row else renderer.text_color)
            t = renderer.font.render(text_val, True, color)
            ty = y + (renderer.line_height - t.get_height()) // 2
            panel.blit(t, (cx, ty))
            renderer.last_blits.append((cx, ty))
            cell_rect = pygame.Rect(cx - 4, y - 2, col_widths[c] + 8, renderer.line_height + 4)
            is_hover = (hovered_row == r and hovered_col == c)
            is_sel_cell = (selected_row == r and selected_col == c)
            if is_hover or is_sel_cell:
                pygame.draw.rect(panel, renderer.border_color, cell_rect, width=2, border_radius=6)
            screen_rect = cell_rect.move(panel_rect.topleft)
            renderer.last_table_layout['cell_rects'][(r, c)] = screen_rect
            cx += col_widths[c] + col_gap
        y += block_h

    renderer.last_table_layout['start_row'] = start
    renderer.last_table_layout['end_row'] = end

    if total_rows > max_visible:
        track_rect = pygame.Rect(
            w - renderer.padding_x // 2 - 6,
            renderer.padding_y + tabs_h + renderer.item_gap // 2 + header_h + renderer.item_gap,
            6,
            inner_height,
        )
        draw_scrollbar(renderer, panel, track_rect, max_visible=max_visible, total=total_rows, start_index=start)

    screen.blit(get_surface(panel), panel_rect.topleft)
    return overlay_rect


def draw_table(
    renderer,
    screen: pygame.Surface,
    headers: list[str],
    rows: list[list[str]],
    selected_row: int = 0,
    selected_col: int = 0,
    row_scroll_offset: int = 0,
    hovered_row: int | None = None,
    hovered_col: int | None = None,
) -> pygame.Rect:
    overlay_rect = draw_overlay(renderer, screen)

    ncols = len(headers)
    col_gap = max(20, renderer.padding_x - 8)
    col_widths = [0] * max(1, ncols)
    for i, htxt in enumerate(headers):
        tw, _ = renderer.font.size(htxt)
        col_widths[i] = max(col_widths[i], tw)
    for row in rows:
        for i, cell in enumerate(row[:ncols]):
            tw, _ = renderer.font.size(cell)
            col_widths[i] = max(col_widths[i], tw)

    inner_w = sum(col_widths) + col_gap * max(0, (ncols - 1))
    w = renderer.padding_x * 2 + inner_w
    total_rows = len(rows)
    header_h = renderer.line_height
    rows_h = (total_rows or 1) * renderer.line_height + max(0, (total_rows - 1)) * renderer.item_gap
    h = renderer.padding_y * 2 + header_h + renderer.item_gap + rows_h

    sw, sh = screen.get_size()
    w = min(w, int(sw * 0.95))
    h = min(h, int(sh * 0.85))
    panel_rect = pygame.Rect((sw - w) // 2, (sh - h) // 2, w, h)

    draw_shadow(renderer, screen, panel_rect)
    panel = draw_panel(renderer, (w, h))

    renderer.last_blits = []
    x = renderer.padding_x
    y = renderer.padding_y
    for i, htxt in enumerate(headers):
        t = renderer.font.render(htxt, True, renderer.text_color_dim)
        ty = y + (renderer.line_height - t.get_height()) // 2
        panel.blit(t, (x, ty))
        renderer.last_blits.append((x, ty))
        x += col_widths[i] + col_gap

    sep_y = y + header_h + (renderer.item_gap // 2)
    pygame.draw.line(panel, (255, 255, 255, 35), (renderer.padding_x, sep_y), (w - renderer.padding_x, sep_y), 1)

    inner_height = h - renderer.padding_y * 2 - header_h - renderer.item_gap
    block_h = renderer.line_height + renderer.item_gap
    max_visible = max(1, (inner_height + renderer.item_gap) // block_h)
    if total_rows <= max_visible:
        start = 0
        end = total_rows
    else:
        max_offset = max(0, total_rows - max_visible)
        row_scroll_offset = max(0, min(row_scroll_offset, max_offset))
        start = row_scroll_offset
        end = start + max_visible

    renderer.last_table_layout = {
        'panel_rect': panel_rect,
        'start_row': 0,
        'end_row': 0,
        'cell_rects': {},
    }

    y = renderer.padding_y + header_h + renderer.item_gap
    for r in range(start, end):
        cells = rows[r]
        is_sel_row = (r == selected_row)
        if is_sel_row:
            pill_rect = pygame.Rect(renderer.padding_x, y, w - renderer.padding_x * 2, renderer.line_height)
            pygame.draw.rect(panel, renderer.highlight_color, pill_rect, border_radius=renderer.radius // 2)
            accent_rect = pygame.Rect(renderer.padding_x - 6, y, 4, renderer.line_height)
            pygame.draw.rect(panel, renderer.accent_color, accent_rect, border_radius=2)
        cx = renderer.padding_x
        for c in range(ncols):
            text_val = cells[c] if c < len(cells) else ""
            color = renderer.accent_color if (is_sel_row and c == selected_col) else (renderer.accent_color if is_sel_row else renderer.text_color)
            t = renderer.font.render(text_val, True, color)
            ty = y + (renderer.line_height - t.get_height()) // 2
            panel.blit(t, (cx, ty))
            renderer.last_blits.append((cx, ty))
            cell_rect = pygame.Rect(cx - 4, y - 2, col_widths[c] + 8, renderer.line_height + 4)
            is_hover = (hovered_row == r and hovered_col == c)
            is_sel_cell = (selected_row == r and selected_col == c)
            if is_hover or is_sel_cell:
                pygame.draw.rect(panel, renderer.border_color, cell_rect, width=2, border_radius=6)
            screen_rect = cell_rect.move(panel_rect.topleft)
            renderer.last_table_layout['cell_rects'][(r, c)] = screen_rect
            cx += col_widths[c] + col_gap
        y += block_h

    renderer.last_table_layout['start_row'] = start
    renderer.last_table_layout['end_row'] = end

    if total_rows > max_visible:
        track_rect = pygame.Rect(w - renderer.padding_x // 2 - 6, renderer.padding_y + header_h + renderer.item_gap, 6, inner_height)
        draw_scrollbar(renderer, panel, track_rect, max_visible=max_visible, total=total_rows, start_index=start)

    screen.blit(get_surface(panel), panel_rect.topleft)
    return overlay_rect
