from __future__ import annotations

import pygame
import math
from typing import Tuple

from .lighting_state import LightingEditorState


class LightingEditorView:
    def __init__(self, state: LightingEditorState, font: pygame.font.Font | None = None) -> None:
        self.state = state
        self.font = font or pygame.font.SysFont("consolas", 18)

    def render(self, screen: pygame.Surface, *, ambient_on: bool, lights_on: bool) -> None:
        st = self.state
        x = st.panel_x
        y = st.panel_y
        w = st.panel_w
        row = st.row_h
        # Panel background
        panel_h = row * 5 + 12
        bg = pygame.Surface((w, panel_h), pygame.SRCALPHA)
        bg.fill((20, 20, 28, 200))
        screen.blit(bg, (x, y))
        st._panel_rect = pygame.Rect(x, y, w, panel_h)
        # Title
        self._draw_label(screen, x + 8, y + 6, "Lighting Editor", (220, 220, 235))
        # Buttons
        by = y + 6 + row
        st._btn_ambient = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Ambient: {'ON' if ambient_on else 'OFF'}", ambient_on)
        by += row
        st._btn_lights = self._draw_button(screen, x + 8, by, w - 16, row - 6, f"Point Lights: {'ON' if lights_on else 'OFF'}", lights_on)
        by += row
        # Spawn button: when active, blink in yellow
        spawn_label = "Spawn Debug Light (Click map)"
        if getattr(st, 'spawn_mode', False):
            t = pygame.time.get_ticks() * 0.012
            pulse = 0.5 + 0.5 * math.sin(t)
            base = 140 + int(80 * pulse)
            bg_col = (base, base, 40, 230)
            border_col = (255, 235, 80)
            st._btn_spawn = self._draw_button(screen, x + 8, by, w - 16, row - 6, spawn_label, True, bg_color=bg_col, border_color=border_col)
        else:
            st._btn_spawn = self._draw_button(screen, x + 8, by, w - 16, row - 6, spawn_label, False)
        by += row
        st._btn_clear = self._draw_button(screen, x + 8, by, w - 16, row - 6, "Clear Debug Lights", False)

    def _draw_label(self, screen: pygame.Surface, x: int, y: int, text: str, color: Tuple[int, int, int]) -> None:
        surf = self.font.render(text, True, color)
        screen.blit(surf, (x, y))

    def _draw_button(self, screen: pygame.Surface, x: int, y: int, w: int, h: int, text: str, on: bool, *, bg_color: Tuple[int,int,int,int] | None = None, border_color: Tuple[int,int,int] | None = None) -> pygame.Rect:
        rect = pygame.Rect(x, y, w, h)
        # Background color by state
        bg_on = (60, 120, 60, 220)
        bg_off = (120, 60, 60, 220)
        bg = pygame.Surface((w, h), pygame.SRCALPHA)
        fill_col = bg_color if bg_color is not None else (bg_on if on else bg_off)
        bg.fill(fill_col)
        screen.blit(bg, rect.topleft)
        # Border
        pygame.draw.rect(screen, border_color if border_color is not None else (30, 30, 30), rect, width=2)
        # Text
        surf = self.font.render(text, True, (250, 250, 250))
        tx = x + 10
        ty = y + (h - surf.get_height()) // 2
        screen.blit(surf, (tx, ty))
        return rect
