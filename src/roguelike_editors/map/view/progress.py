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
        label_pos = (bar_x, bar_y - bar_h - 2)
        screen.blit(text_surf, label_pos)

        # Draw Pause/Resume and Cancel buttons above the bar, aligned to the right edge
        btn_h = max(20, int(self.fonts.small.get_height() + 8))
        pause_txt = "Reanudar" if getattr(state, 'execution_paused', False) else "Pausar"
        pause_surf = self.fonts.small.render(pause_txt, True, self.palette.text)
        cancel_surf = self.fonts.small.render("Cancelar", True, self.palette.text)
        pad = 8
        cancel_w = cancel_surf.get_width() + 14
        pause_w = pause_surf.get_width() + 14
        btn_y = bar_y - btn_h - 6
        cancel_x = bar_x + bar_w - cancel_w
        pause_x = cancel_x - pad - pause_w
        pause_rect = pygame.Rect(int(pause_x), int(btn_y), int(pause_w), int(btn_h))
        cancel_rect = pygame.Rect(int(cancel_x), int(btn_y), int(cancel_w), int(btn_h))

        # Buttons background and borders
        pygame.draw.rect(screen, self.palette.progress_bg, pause_rect)
        pygame.draw.rect(screen, self.palette.text, pause_rect, 1)
        pygame.draw.rect(screen, self.palette.progress_bg, cancel_rect)
        pygame.draw.rect(screen, self.palette.text, cancel_rect, 1)

        # Buttons labels centered
        screen.blit(pause_surf, pause_surf.get_rect(center=pause_rect.center))
        screen.blit(cancel_surf, cancel_surf.get_rect(center=cancel_rect.center))

        # Expose rects on state for event handling
        try:
            state.progress_pause_rect = pause_rect
            state.progress_cancel_rect = cancel_rect
        except Exception:
            pass
