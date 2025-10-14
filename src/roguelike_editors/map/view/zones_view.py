from __future__ import annotations
from typing import Optional, Tuple
import pygame
from pygame import Surface, Rect

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.tiles.tiles_editor_config import BTN_H

from .fonts import Fonts
from .colors import Palette


class ZonesView:
    """Renders zones (rectangles), labels and renaming overlay."""

    def __init__(self, fonts: Fonts, palette: Palette) -> None:
        self.fonts = fonts
        self.palette = palette

    def render(self, screen: Surface, camera, state) -> Optional[pygame.Rect]:
        zones = global_map_settings.zone_offsets
        zone_w, zone_h = global_map_settings.zone_size
        last_selected_rect: Optional[pygame.Rect] = None

        for zone_name, (ox, oy) in zones.items():
            if zone_name in ("no zone", "no-zone"):
                continue

            hidden = zone_name in state.hidden_zones
            if hidden:
                outline_color = self.palette.border_hidden
                fill_color = (*outline_color, 50)
            else:
                outline_color = (
                    self.palette.border_selected if zone_name == state.selected_zone else self.palette.border_default
                )
                fill_color = (*outline_color, 50)

            px, py = ox * TILE_SIZE, oy * TILE_SIZE
            pw, ph = zone_w * TILE_SIZE, zone_h * TILE_SIZE

            screen_tl = camera.apply((px, py))
            screen_size = camera.scale((pw, ph))

            # Fill
            surf = Surface(screen_size, pygame.SRCALPHA)
            surf.fill(fill_color)
            screen.blit(surf, screen_tl)

            # Outline
            pygame.draw.rect(screen, outline_color, (*screen_tl, *screen_size), 2)

            if zone_name == state.selected_zone:
                last_selected_rect = pygame.Rect(*screen_tl, *screen_size)

            if state.pending_delete_zone == zone_name:
                red_outline = self.palette.border_delete
                red_fill = (255, 0, 0, 50)
                surf_del = Surface(screen_size, pygame.SRCALPHA)
                surf_del.fill(red_fill)
                screen.blit(surf_del, screen_tl)
                pygame.draw.rect(screen, red_outline, (*screen_tl, *screen_size), 2)
                continue

            if state.renaming_zone == zone_name:
                self._draw_renaming_overlay(screen, state, zone_name, screen_tl, screen_size)
            else:
                self._draw_zone_label(screen, screen_tl, screen_size, zone_name)

        return last_selected_rect

    def _draw_zone_label(
        self, screen: Surface, screen_tl: Tuple[float, float], screen_size: Tuple[int, int], text: str
    ) -> None:
        label_surf = self.fonts.large.render(text, True, self.palette.text)
        label_w, label_h = label_surf.get_size()
        max_w, max_h = screen_size

        if label_w > max_w or label_h > max_h:
            scale = min(max_w / label_w, max_h / label_h)
            new_size = (int(label_w * scale), int(label_h * scale))
            label_surf = pygame.transform.smoothscale(label_surf, new_size)
            label_w, label_h = new_size

        x = screen_tl[0] + (screen_size[0] - label_w) / 2
        y = screen_tl[1] + (screen_size[1] - label_h) / 2
        screen.blit(label_surf, (x, y))

    def _draw_renaming_overlay(
        self,
        screen: Surface,
        state,
        zone_name: str,
        screen_tl: Tuple[float, float],
        screen_size: Tuple[int, int],
    ) -> None:
        text_input = state.rename_input or ""
        input_surf = self.fonts.large.render(text_input, True, (0, 0, 0))
        text_h = input_surf.get_height()
        padding_y = 4

        box_h = max(text_h + padding_y * 2, BTN_H)
        total_w = screen_size[0]

        accept_w = box_h * 2
        input_w = max(20, total_w - accept_w - 5)
        input_x = screen_tl[0]
        input_y = screen_tl[1] + screen_size[1] - box_h - 5

        input_rect = Rect(input_x, input_y, input_w, box_h)
        pygame.draw.rect(screen, self.palette.input_bg, input_rect)
        pygame.draw.rect(screen, self.palette.input_border, input_rect, 2)
        screen.blit(input_surf, (input_x + 5, input_y + (box_h - text_h) // 2))
        state.rename_input_rect = input_rect

        accept_rect = Rect(input_rect.right + 5, input_y, accept_w, box_h)
        pygame.draw.rect(screen, self.palette.button_bg, accept_rect)
        pygame.draw.rect(screen, self.palette.button_border, accept_rect, 2)
        btn_font = pygame.font.SysFont(None, int(box_h * 0.6))
        ok_surf = btn_font.render("Aceptar", True, self.palette.button_text)
        screen.blit(
            ok_surf,
            (accept_rect.centerx - ok_surf.get_width() // 2, accept_rect.centery - ok_surf.get_height() // 2),
        )
        state.rename_accept_rect = accept_rect

        now = pygame.time.get_ticks()
        if (now // 500) % 2 == 0:
            caret_x = input_x + 5 + input_surf.get_width()
            caret_y1 = input_y + padding_y
            caret_y2 = input_y + box_h - padding_y
            pygame.draw.line(screen, (0, 0, 0), (caret_x, caret_y1), (caret_x, caret_y2), 2)
