from __future__ import annotations

import pygame
from .utils import get_surface, clamp


def draw_button(renderer, panel: pygame.Surface, rect: pygame.Rect, text_surface: pygame.Surface, *, hover: bool = False, active: bool = False) -> None:
    pygame.draw.rect(panel, renderer.button_bg, rect, border_radius=renderer.radius // 2)
    if hover or active:
        pygame.draw.rect(panel, renderer.border_color, rect, width=2, border_radius=renderer.radius // 2)
    tx = rect.x + (rect.width - text_surface.get_width()) // 2
    ty = rect.y + (rect.height - text_surface.get_height()) // 2
    panel.blit(text_surface, (tx, ty))


def draw_overlay(renderer, screen: pygame.Surface) -> pygame.Rect:
    w, h = screen.get_size()
    overlay = pygame.Surface((w, h), pygame.SRCALPHA)
    overlay.fill(renderer.overlay_color)
    screen.blit(get_surface(overlay), (0, 0))
    return pygame.Rect(0, 0, w, h)


def draw_shadow(renderer, screen: pygame.Surface, rect: pygame.Rect) -> None:
    sx, sy = renderer.shadow_offset
    shadow_rect = rect.move(sx, sy)
    shadow_surf = pygame.Surface((shadow_rect.width, shadow_rect.height), pygame.SRCALPHA)
    pygame.draw.rect(shadow_surf, (0, 0, 0, 110), shadow_surf.get_rect(), border_radius=renderer.radius + 2)
    screen.blit(get_surface(shadow_surf), shadow_rect.topleft)


def draw_panel(renderer, size: tuple[int, int]) -> pygame.Surface:
    w, h = size
    panel = pygame.Surface((w, h), pygame.SRCALPHA)
    rect = panel.get_rect()
    color = (*renderer.panel_bg, renderer.panel_alpha)
    pygame.draw.rect(panel, color, rect, border_radius=renderer.radius)
    return panel


def measure_menu(renderer, options: list[str]) -> tuple[int, int]:
    max_w = 0
    for opt in options:
        tw, _ = renderer.font.size(opt)
        max_w = max(max_w, tw)
    width = renderer.padding_x * 2 + max_w + 8
    if options:
        inner_h = len(options) * renderer.line_height + (len(options) - 1) * renderer.item_gap
    else:
        inner_h = renderer.line_height
    height = renderer.padding_y * 2 + inner_h
    return width, height


def center_rect(renderer, screen: pygame.Surface, size: tuple[int, int]) -> pygame.Rect:
    sw, sh = screen.get_size()
    w, h = size
    x = (sw - w) // 2
    y = (sh - h) // 2
    return pygame.Rect(x, y, w, h)


def draw_scrollbar(renderer, panel: pygame.Surface, track_rect: pygame.Rect, *, max_visible: int, total: int, start_index: int) -> None:
    if total <= max_visible or track_rect.height <= 0:
        return
    pygame.draw.rect(panel, (255, 255, 255, 28), track_rect, border_radius=3)
    thumb_h = max(24, int(track_rect.height * (max_visible / max(1, total))))
    max_thumb_top = track_rect.y + track_rect.height - thumb_h
    if (total - max_visible) == 0:
        thumb_top = track_rect.y
    else:
        thumb_top = int(track_rect.y + (track_rect.height - thumb_h) * (start_index / max(1, (total - max_visible))))
    thumb_top = int(clamp(thumb_top, track_rect.y, max_thumb_top))
    pygame.draw.rect(panel, renderer.accent_color, pygame.Rect(track_rect.x, thumb_top, track_rect.width, thumb_h), border_radius=3)
