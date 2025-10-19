import pygame
from typing import Tuple

from roguelike_engine.config.config_minimap import (
    MINIMAP_COLORS,
    MINIMAP_ZONE_COLORS,
    MINIMAP_ZONE_BORDER_WIDTH,
    MINIMAP_BTN_SIZE,
    MINIMAP_BTN_MARGIN,
    MINIMAP_BTN_BG_ACTIVE,
    MINIMAP_BTN_BG_INACTIVE,
    MINIMAP_BTN_BORDER,
    MINIMAP_BTN_BORDER_HOVER,
    MINIMAP_BTN_TEXT,
)


class MinimapView:
    """
    Vista del Minimap. Se encarga de componer las capas en la surface final,
    dibujar UI (botones) y blitear en pantalla.
    """

    def render(self, screen: pygame.Surface, model) -> pygame.Rect:
        # Limpiar surface final
        model.surface.fill((0, 0, 0, 0))

        # Componer capas
        if model.show_tiles:
            model.surface.blit(model.bg_tiles_surface, (0, 0))
        if model.show_buildings:
            model.surface.blit(model.buildings_surface, (0, 0))
        if model.show_zones:
            model.surface.blit(model.zones_surface, (0, 0))
        if model.show_entities:
            model.surface.blit(model.entities_surface, (0, 0))

        # Jugador centrado
        pygame.draw.rect(
            model.surface,
            MINIMAP_COLORS["player"],
            (model.width // 2, model.height // 2, model.zoom, model.zoom),
        )

        # Fuente para botones
        if model.font is None:
            try:
                model.font = pygame.font.SysFont("Arial", 12)
            except Exception:
                model.font = pygame.font.Font(None, 12)

        # Botones de capas
        btn_w, btn_h = MINIMAP_BTN_SIZE
        x0, y0 = MINIMAP_BTN_MARGIN, MINIMAP_BTN_MARGIN
        buttons = [
            ("tiles", "T", model.show_tiles),
            ("buildings", "B", model.show_buildings),
            ("zones", "Z", model.show_zones),
            ("entities", "E", model.show_entities),
        ]
        model.btn_rects.clear()
        cur_x = x0
        for key, label, active in buttons:
            rect = pygame.Rect(cur_x, y0, btn_w, btn_h)
            model.btn_rects[key] = rect
            bg = MINIMAP_BTN_BG_ACTIVE if active else MINIMAP_BTN_BG_INACTIVE
            pygame.draw.rect(model.surface, bg, rect, border_radius=3)
            border_col = MINIMAP_BTN_BORDER_HOVER if model.btn_hover == key else MINIMAP_BTN_BORDER
            pygame.draw.rect(model.surface, border_col, rect, width=1, border_radius=3)
            try:
                txt = model.font.render(label, True, MINIMAP_BTN_TEXT)
                tr = txt.get_rect(center=rect.center)
                model.surface.blit(txt, tr)
            except Exception:
                pass
            cur_x += btn_w + MINIMAP_BTN_MARGIN

        # Posición final (esquina superior derecha)
        dest = (screen.get_width() - model.width - model.pad_x, model.pad_y)
        screen.blit(model.surface, dest)

        model.last_rect = pygame.Rect(dest, (model.width, model.height))
        return model.last_rect

    def get_rect(self, screen: pygame.Surface, model) -> pygame.Rect:
        return pygame.Rect((screen.get_width() - model.width - model.pad_x, model.pad_y), (model.width, model.height))
