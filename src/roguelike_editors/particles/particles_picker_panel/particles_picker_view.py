import pygame
from typing import Dict, Tuple

from .particles_picker_model import ParticlesPickerModel


class ParticlesPickerView:
    """Simple grid view to show particle presets with animated previews."""

    def __init__(self, model: ParticlesPickerModel, font: pygame.font.Font | None):
        self.model = model
        self.font = None  # disable labels under cells
        self.title_font = font

    def draw(self, screen: pygame.Surface, dt_ms: int = 16) -> None:
        items = list(self.model.items.items())
        if not items:
            return
        cell = self.model.cell_size
        margin = self.model.cell_margin
        cols = max(1, int(self.model.columns))
        gx, gy = self.model.grid_origin

        # Build groups: kind -> list of (pid, def)
        groups: Dict[str, list[tuple[str, dict]]] = {}
        if getattr(self.model, 'group_by_kind', False):
            for pid, pdef in items:
                kind = str(pdef.get("kind") or "other")
                groups.setdefault(kind, []).append((pid, pdef))
            # Deterministic order: kind name ascending
            ordered = sorted(groups.items(), key=lambda kv: kv[0])
        else:
            # Single pseudo-group preserving original order
            groups = {"all": items}
            ordered = [("all", items)]

        # First pass: compute total content height
        header_h = 18
        y_cursor = gy
        total_w = cols * cell + (cols - 1) * margin
        for _kind, pairs in ordered:
            if getattr(self.model, 'group_by_kind', False):
                y_cursor += header_h + 4  # header + small gap
            count = len(pairs)
            rows = (count + cols - 1) // cols
            if rows > 0:
                y_cursor += rows * cell + (rows - 1) * margin
            # gap between groups
            y_cursor += 8
        content_h = max(0, y_cursor - gy)
        self.model.content_height = content_h
        # Determine viewport height (visible area)
        try:
            screen_h = screen.get_height()
        except Exception:
            screen_h = gy + 300
        viewport_h = max(120, min(content_h if content_h > 0 else 300, screen_h - gy - 24))
        self.model.viewport_height = viewport_h
        # Clamp scroll
        max_scroll = max(0, content_h - viewport_h)
        sy = int(getattr(self.model, 'scroll_y', 0))
        if sy < 0:
            sy = 0
        if sy > max_scroll:
            sy = max_scroll
        self.model.scroll_y = sy

        # Visible viewport and panel rect
        viewport_rect = pygame.Rect(gx, gy, total_w, viewport_h)
        self.model.grid_rect = viewport_rect
        panel_rect = viewport_rect.inflate(8, 8)

        # Draw background panel and add-mode blink border
        pygame.draw.rect(screen, (25, 25, 25), panel_rect, border_radius=6)
        if getattr(self.model, 'add_mode_active', False):
            t = pygame.time.get_ticks()
            on = (t // 250) % 2 == 0
            color = (255, 220, 0) if on else (160, 130, 0)
            try:
                pygame.draw.rect(screen, color, panel_rect, width=3, border_radius=8)
            except Exception:
                pass

        # Toggle button (above the panel, anchored to right)
        btn_w, btn_h = 64, 20
        btn_x = panel_rect.right - btn_w - 2
        btn_y = max(0, panel_rect.top - btn_h - 6)
        toggle_rect = pygame.Rect(btn_x, btn_y, btn_w, btn_h)
        self.model.toggle_rect = toggle_rect
        try:
            active = bool(getattr(self.model, 'group_by_kind', False))
            bg = (60, 60, 70) if active else (40, 40, 45)
            pygame.draw.rect(screen, bg, toggle_rect, border_radius=6)
            pygame.draw.rect(screen, (80, 80, 90), toggle_rect, width=1, border_radius=6)
            label = "GROUP" if active else "ALL"
            if self.title_font:
                txt = self.title_font.render(label, True, (230, 230, 230))
                tr = txt.get_rect(center=toggle_rect.center)
                screen.blit(txt, tr)
        except Exception:
            pass

        # Second pass: draw headers and cells, populate hit rects within viewport
        self.model.cell_rects.clear()
        old_clip = screen.get_clip()
        screen.set_clip(viewport_rect)
        y_cursor = gy
        for kind, pairs in ordered:
            if getattr(self.model, 'group_by_kind', False):
                # Header bar
                hdr_rect = pygame.Rect(gx, y_cursor - self.model.scroll_y, total_w, header_h)
                try:
                    pygame.draw.rect(screen, (32, 32, 36), hdr_rect)
                    pygame.draw.rect(screen, (70, 70, 80), hdr_rect, width=1)
                    if self.title_font:
                        txt = self.title_font.render(str(kind).upper(), True, (210, 210, 220))
                        screen.blit(txt, (hdr_rect.x + 6, hdr_rect.y + 1))
                except Exception:
                    pass
                y_cursor += header_h + 4

            # Draw group's grid
            for idx, (pid, pdef) in enumerate(pairs):
                r = idx // cols
                c = idx % cols
                x = gx + c * (cell + margin)
                y = y_cursor + r * (cell + margin) - self.model.scroll_y
                rect = pygame.Rect(x, y, cell, cell)
                self.model.cell_rects[pid] = rect
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
                # Overlays
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
                elif self.model.selected_id == pid:
                    try:
                        pygame.draw.rect(screen, (255, 220, 0), rect, width=3, border_radius=6)
                    except Exception:
                        pass

            # Advance y past this group's grid
            count = len(pairs)
            rows = (count + cols - 1) // cols
            if rows > 0:
                y_cursor += rows * cell + (rows - 1) * margin
            y_cursor += 8
        screen.set_clip(old_clip)
