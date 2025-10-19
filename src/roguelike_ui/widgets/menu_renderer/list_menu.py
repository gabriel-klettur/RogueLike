from __future__ import annotations

import pygame
from .core import draw_overlay, draw_shadow, draw_panel, measure_menu, center_rect, draw_scrollbar
from .utils import get_surface


def draw(renderer, screen: pygame.Surface, selected: int, options: list[str], scroll_offset: int = 0, panel_top_min: int | None = None) -> pygame.Rect:
    overlay_rect = draw_overlay(renderer, screen)

    sw, sh = screen.get_size()
    w, h = measure_menu(renderer, options)
    w = min(w, int(sw * 0.95))
    h = min(h, int(sh * 0.85))
    panel_rect = center_rect(renderer, screen, (w, h))
    if isinstance(panel_top_min, int):
        extra = max(24, int(renderer.line_height))
        desired_top = panel_top_min + extra
        if panel_rect.top < desired_top:
            panel_rect.top = desired_top
    if panel_rect.bottom > (sh - 8):
        panel_rect.bottom = sh - 8
    renderer.last_menu_panel_rect = panel_rect

    draw_shadow(renderer, screen, panel_rect)
    panel = draw_panel(renderer, (w, h))

    renderer.last_blits = []
    total = len(options)
    inner_height = h - renderer.padding_y * 2
    block_h = renderer.line_height + renderer.item_gap
    max_visible = max(1, (inner_height + renderer.item_gap) // block_h)

    if total <= max_visible:
        start = 0
        end = total
    else:
        max_offset = max(0, total - max_visible)
        scroll_offset = max(0, min(scroll_offset, max_offset))
        start = scroll_offset
        end = start + max_visible

    y = renderer.padding_y
    for i in range(start, end):
        option = options[i]
        is_sel = (i == selected)
        if is_sel:
            pill_rect = pygame.Rect(0, 0, w - renderer.padding_x * 2, renderer.line_height)
            pill_rect.topleft = (renderer.padding_x, y)
            pygame.draw.rect(panel, renderer.highlight_color, pill_rect, border_radius=renderer.radius // 2)
            accent_rect = pygame.Rect(renderer.padding_x - 6, y, 4, renderer.line_height)
            pygame.draw.rect(panel, renderer.accent_color, accent_rect, border_radius=2)
        color = renderer.accent_color if is_sel else renderer.text_color
        text = renderer.font.render(option, True, color)
        tx = renderer.padding_x + 12
        ty = y + (renderer.line_height - text.get_height()) // 2
        panel.blit(text, (tx, ty))
        renderer.last_blits.append((tx, ty))
        y += block_h

    if total > max_visible:
        track_rect = pygame.Rect(w - renderer.padding_x // 2 - 6, renderer.padding_y, 6, inner_height)
        draw_scrollbar(renderer, panel, track_rect, max_visible=max_visible, total=total, start_index=start)

    screen.blit(get_surface(panel), panel_rect.topleft)
    return overlay_rect
