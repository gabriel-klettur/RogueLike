from __future__ import annotations

import pygame
from typing import Tuple

from roguelike_editors.entities.entities_properties_panel.services.state_tabs_helpers import (
    build_tab_rects,
    format_tab_label,
)


def render_tabs(
    screen: pygame.Surface,
    font: pygame.font.Font,
    model: object,
    panel_pos: Tuple[int, int],
    pad: int,
    tab_pad: Tuple[int, int],
) -> int:
    """Render the top tabs and return their height in pixels.

    Side effects:
      - Sets model.type_tab_rects
    """
    panel_x, panel_y = panel_pos
    model.type_tab_rects = build_tab_rects(
        model.type_tabs, font, (panel_x + pad, panel_y + pad), tab_pad
    )
    any_tab_rect = next(iter(model.type_tab_rects.values())) if model.type_tab_rects else pygame.Rect(0, 0, 0, 0)
    tabs_h = any_tab_rect.h if model.type_tab_rects else 0

    mouse_pos = pygame.mouse.get_pos()
    for label, rect in model.type_tab_rects.items():
        is_active = (model.active_type_tab == label)
        is_hover = rect.collidepoint(mouse_pos)
        if is_active or is_hover:
            surf = pygame.Surface((rect.w, rect.h), pygame.SRCALPHA)
            surf.fill((255, 255, 0, 100))
            screen.blit(surf, (rect.x, rect.y))
            pygame.draw.rect(screen, (255, 255, 0), rect, 2)
        else:
            pygame.draw.rect(screen, (100, 100, 100), rect)
            pygame.draw.rect(screen, (255, 255, 255), rect, 2)
        text_label = format_tab_label(label)
        text_surf = font.render(text_label, True, (0, 0, 0))
        text_x = rect.x + (rect.w - text_surf.get_width()) // 2
        text_y = rect.y + tab_pad[1]
        screen.blit(text_surf, (text_x, text_y))

    return tabs_h
