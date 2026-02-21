from __future__ import annotations

from typing import Callable, List, Tuple

import pygame


def compute_fixed_layout(
    *,
    renderer,
    screen: pygame.Surface,
    tabs: List[Tuple[str, str]],  # (label, key)
    headers: List[str],
    build_rows_for_tab: Callable[[str], list[list[str]]],
) -> tuple[tuple[int, int], list[int], tuple[int, int]]:
    """Compute fixed column widths and panel size across all tabs.

    Returns a tuple: (fixed_screen_size, fixed_col_widths, fixed_panel_size).
    - fixed_screen_size: current screen size (w, h) to detect future changes.
    - fixed_col_widths: measured widths per column.
    - fixed_panel_size: (w, h) clamped relative to screen.
    """
    ncols = len(headers)
    col_gap = max(20, renderer.padding_x - 8)
    col_widths = [0] * ncols

    # Measure headers
    for i, htxt in enumerate(headers):
        tw, _ = renderer.font.size(htxt)
        col_widths[i] = max(col_widths[i], tw)

    # Measure tabs label width to ensure panel accommodates tabs row
    tab_label_ws = [renderer.font.size(lbl)[0] for (lbl, _k) in tabs]
    tabs_w = sum((w + 14 * 2) for w in tab_label_ws) + 10 * max(0, len(tabs) - 1)

    # Traverse tabs rows
    max_total_rows = 0
    for (_lbl, key) in tabs:
        rows = build_rows_for_tab(key)
        max_total_rows = max(max_total_rows, len(rows))
        for row in rows:
            for i in range(ncols):
                cell = row[i] if i < len(row) else ""
                tw, _ = renderer.font.size(cell)
                col_widths[i] = max(col_widths[i], tw)

    inner_w = sum(col_widths) + col_gap * max(0, (ncols - 1))
    w = renderer.padding_x * 2 + max(inner_w, tabs_w)

    # Height based on max rows (clamp to 85% of screen height)
    header_h = renderer.line_height
    tabs_h = renderer.line_height
    rows_h = (max_total_rows or 1) * renderer.line_height + max(0, (max_total_rows - 1)) * renderer.item_gap
    h = (renderer.padding_y * 2 + tabs_h + renderer.item_gap // 2 + header_h + renderer.item_gap + rows_h)

    sw, sh = screen.get_size()
    w = min(w, int(sw * 0.95))
    h = min(h, int(sh * 0.85))

    fixed_screen_size: tuple[int, int] = (sw, sh)
    fixed_col_widths: list[int] = col_widths
    fixed_panel_size: tuple[int, int] = (w, h)
    return fixed_screen_size, fixed_col_widths, fixed_panel_size
