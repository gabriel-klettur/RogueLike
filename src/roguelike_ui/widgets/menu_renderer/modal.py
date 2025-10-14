from __future__ import annotations

import pygame
from .core import draw_overlay, draw_shadow, draw_panel
from .utils import get_surface


def draw_confirm_dialog(renderer, screen: pygame.Surface, lines: list[str], *, hover_yes: bool = False, hover_cancel: bool = False) -> pygame.Rect:
    overlay_rect = draw_overlay(renderer, screen)

    max_w = 0
    for line in lines:
        tw, _ = renderer.font.size(line)
        max_w = max(max_w, tw)
    yes_t = renderer.font.render("Sí, borrar", True, renderer.text_color)
    cancel_t = renderer.font.render("Cancelar", True, renderer.text_color)
    pad_btn_x = 18
    btn_h = renderer.line_height
    yes_w = yes_t.get_width() + pad_btn_x * 2
    cancel_w = cancel_t.get_width() + pad_btn_x * 2
    gap = 20
    buttons_w = yes_w + gap + cancel_w

    w = renderer.padding_x * 2 + max(max_w, buttons_w)
    rows_h = (len(lines) or 1) * renderer.line_height + max(0, (len(lines) - 1)) * (renderer.item_gap - 2)
    h = renderer.padding_y * 2 + rows_h + renderer.item_gap + btn_h
    sw, sh = screen.get_size()
    w = min(w, int(sw * 0.8))
    h = min(h, int(sh * 0.5))

    x = (sw - w) // 2
    y = (sh - h) // 2
    panel_rect = pygame.Rect(x, y, w, h)

    draw_shadow(renderer, screen, panel_rect)
    panel = draw_panel(renderer, (w, h))

    yoff = renderer.padding_y
    for line in lines:
        t = renderer.font.render(line, True, renderer.text_color)
        ty = yoff + (renderer.line_height - t.get_height()) // 2
        panel.blit(t, (renderer.padding_x, ty))
        yoff += renderer.line_height + (renderer.item_gap - 2)

    btn_y = h - renderer.padding_y - btn_h
    base_x = (w - buttons_w) // 2
    yes_rect_local = pygame.Rect(base_x, btn_y, yes_w, btn_h)
    cancel_rect_local = pygame.Rect(base_x + yes_w + gap, btn_y, cancel_w, btn_h)
    btn_bg = (255, 255, 255, 22)

    # Sí
    pygame.draw.rect(panel, btn_bg, yes_rect_local, border_radius=renderer.radius // 2)
    if hover_yes:
        pygame.draw.rect(panel, renderer.border_color, yes_rect_local, width=2, border_radius=renderer.radius // 2)
    yx = yes_rect_local.x + (yes_rect_local.width - yes_t.get_width()) // 2
    yy = yes_rect_local.y + (yes_rect_local.height - yes_t.get_height()) // 2
    panel.blit(yes_t, (yx, yy))

    # Cancelar
    pygame.draw.rect(panel, btn_bg, cancel_rect_local, border_radius=renderer.radius // 2)
    if hover_cancel:
        pygame.draw.rect(panel, renderer.border_color, cancel_rect_local, width=2, border_radius=renderer.radius // 2)
    cx = cancel_rect_local.x + (cancel_rect_local.width - cancel_t.get_width()) // 2
    cy = cancel_rect_local.y + (cancel_rect_local.height - cancel_t.get_height()) // 2
    panel.blit(cancel_t, (cx, cy))

    screen.blit(get_surface(panel), panel_rect.topleft)
    renderer.last_confirm_layout = {
        'panel_rect': panel_rect,
        'yes_rect': yes_rect_local.move(panel_rect.topleft),
        'cancel_rect': cancel_rect_local.move(panel_rect.topleft),
    }
    return overlay_rect


def draw_message(renderer, screen: pygame.Surface, lines: list[str]) -> pygame.Rect:
    overlay_rect = draw_overlay(renderer, screen)
    max_w = 0
    for line in lines:
        tw, _ = renderer.font.size(line)
        max_w = max(max_w, tw)
    w = renderer.padding_x * 2 + max_w
    rows_h = (len(lines) or 1) * renderer.line_height + max(0, (len(lines) - 1)) * (renderer.item_gap - 2)
    h = renderer.padding_y * 2 + rows_h

    sw, sh = screen.get_size()
    w = min(w, int(sw * 0.9))
    h = min(h, int(sh * 0.6))

    panel_rect = pygame.Rect((sw - w) // 2, (sh - h) // 2, w, h)
    draw_shadow(renderer, screen, panel_rect)
    panel = draw_panel(renderer, (w, h))

    y = renderer.padding_y
    for line in lines:
        t = renderer.font.render(line, True, renderer.text_color)
        ty = y + (renderer.line_height - t.get_height()) // 2
        panel.blit(t, (renderer.padding_x, ty))
        y += renderer.line_height + (renderer.item_gap - 2)

    screen.blit(get_surface(panel), panel_rect.topleft)
    return overlay_rect
