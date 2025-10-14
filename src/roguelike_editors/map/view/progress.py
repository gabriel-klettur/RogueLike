from pygame import Surface
import pygame

from .fonts import Fonts
from .colors import Palette


class ProgressView:
    """Renders loading overlays and bottom progress bar."""

    def __init__(self, fonts: Fonts, palette: Palette) -> None:
        self.fonts = fonts
        self.palette = palette

    def draw_loading_overlay(self, screen: Surface, state) -> None:
        sw, sh = screen.get_size()
        overlay = pygame.Surface((sw, sh), pygame.SRCALPHA)
        overlay.fill(self.palette.overlay_bg)
        screen.blit(overlay, (0, 0))

        bar_w, bar_h = sw * 0.5, 20
        bar_x = (sw - bar_w) / 2
        bar_y = (sh - bar_h) / 2
        pygame.draw.rect(screen, self.palette.progress_bg, (bar_x, bar_y, bar_w, bar_h))

        total = max(getattr(state, "execution_total", 1), 1)
        progress = getattr(state, "execution_index", 0) / total
        fill_w = bar_w * progress
        pygame.draw.rect(screen, self.palette.progress_fill, (bar_x, bar_y, fill_w, bar_h))
        pygame.draw.rect(screen, self.palette.text, (bar_x, bar_y, bar_w, bar_h), 2)

        label = f"{getattr(state, 'executing_tool', '').replace('_', ' ').title()}: {int(progress * 100)}%"
        text_surf = self.fonts.medium.render(label, True, self.palette.text)
        text_x = bar_x + (bar_w - text_surf.get_width()) / 2
        text_y = bar_y + (bar_h - text_surf.get_height()) / 2
        screen.blit(text_surf, (text_x, text_y))

    def draw_bottom_bar(self, screen: Surface, state) -> None:
        sw, sh = screen.get_size()
        bar_w, bar_h = sw * 0.5, 8
        bar_x = (sw - bar_w) / 2
        bar_y = sh * 0.85

        pygame.draw.rect(screen, self.palette.progress_bg, (bar_x, bar_y, bar_w, bar_h))

        total = max(getattr(state, "execution_total", 1), 1)
        progress = getattr(state, "execution_index", 0) / total
        fill_w = bar_w * progress
        pygame.draw.rect(screen, self.palette.progress_fill, (bar_x, bar_y, fill_w, bar_h))
        pygame.draw.rect(screen, self.palette.text, (bar_x, bar_y, bar_w, bar_h), 1)

        label = f"{getattr(state, 'executing_tool', '').replace('_', ' ').title()}: {int(progress * 100)}%"
        text_surf = self.fonts.small.render(label, True, self.palette.text)
        screen.blit(text_surf, (bar_x, bar_y - bar_h - 2))
