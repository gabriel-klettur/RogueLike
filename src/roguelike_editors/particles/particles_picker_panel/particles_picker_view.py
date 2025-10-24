import pygame
from typing import Dict, Tuple

from .particles_picker_model import ParticlesPickerModel


class ParticlesPickerView:
    """Simple grid view to show particle presets with animated previews."""

    def __init__(self, model: ParticlesPickerModel, font: pygame.font.Font | None):
        self.model = model
        self.font = font
        self.title_font = font

    def draw(self, screen: pygame.Surface, dt_ms: int = 16) -> None:
        items = list(self.model.items.items())
        if not items:
            return
        cell = self.model.cell_size
        margin = self.model.cell_margin
        cols = max(1, int(self.model.columns))
        gx, gy = self.model.grid_origin
        # Compute grid rect
        total_w = cols * cell + (cols - 1) * margin
        # Estimate rows
        rows = (len(items) + cols - 1) // cols
        total_h = rows * cell + (rows - 1) * margin
        grid_rect = pygame.Rect(gx, gy, total_w, total_h)
        self.model.grid_rect = grid_rect
        # Draw background
        panel_rect = grid_rect.inflate(8, 8)
        pygame.draw.rect(screen, (25, 25, 25), panel_rect, border_radius=6)
        # Blink border in add-mode to guide user to select a preset
        if getattr(self.model, 'add_mode_active', False):
            t = pygame.time.get_ticks()
            on = (t // 250) % 2 == 0
            color = (255, 220, 0) if on else (160, 130, 0)
            try:
                pygame.draw.rect(screen, color, panel_rect, width=3, border_radius=8)
            except Exception:
                pass
        # Draw cells
        for idx, (pid, pdef) in enumerate(items):
            r = idx // cols
            c = idx % cols
            x = gx + c * (cell + margin)
            y = gy + r * (cell + margin)
            rect = pygame.Rect(x, y, cell, cell)
            # Cell bg
            bg_col = (45, 45, 50)
            if self.model.hovered_id == pid:
                bg_col = (60, 60, 70)
            if self.model.selected_id == pid:
                # Selected highlight; in add mode use yellow-ish background
                if getattr(self.model, 'add_mode_active', False):
                    bg_col = (90, 80, 40)
                else:
                    bg_col = (70, 70, 80)
            pygame.draw.rect(screen, bg_col, rect, border_radius=6)
            pygame.draw.rect(screen, (80, 80, 90), rect, width=1, border_radius=6)
            # Render preview
            provider = self.model.preview_providers.get(pid)
            if provider is not None:
                try:
                    surf = provider((cell - 8, cell - 8), dt_ms)
                    if surf is not None:
                        px = rect.x + 4
                        py = rect.y + 4
                        screen.blit(surf, (px, py))
                except Exception:
                    pass
            # Draw label
            if self.font:
                label = str(pdef.get("name") or pid)
                small = self.font.render(label, True, (220, 220, 220))
                screen.blit(small, (rect.x + 6, rect.bottom + 2))
            # Add-mode blinking overlay and border on the selected cell
            if getattr(self.model, 'add_mode_active', False) and self.model.selected_id == pid:
                t = pygame.time.get_ticks()
                on = (t // 250) % 2 == 0
                # Translucent yellow overlay
                try:
                    overlay = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
                    alpha = 90 if on else 45
                    overlay.fill((255, 220, 0, alpha))
                    screen.blit(overlay, rect.topleft)
                except Exception:
                    pass
                # Yellow border
                try:
                    pygame.draw.rect(screen, (255, 220, 0), rect, width=3, border_radius=6)
                except Exception:
                    pass
            # Always draw a yellow border for the selected cell (regardless of add-mode)
            elif self.model.selected_id == pid:
                try:
                    pygame.draw.rect(screen, (255, 220, 0), rect, width=3, border_radius=6)
                except Exception:
                    pass
