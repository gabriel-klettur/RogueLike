from __future__ import annotations

from typing import Dict, Any, List, Tuple

import pygame
from roguelike_ui.ui_helpers import draw_tooltip
from roguelike_ui.widgets.hover import draw_hover


class SpawnersManagerView:
    def __init__(self) -> None:
        self.panel_rect: pygame.Rect | None = None
        self.content_height: int = 0

    def _flatten(self, data: Dict[str, Any], prefix: str = "") -> List[Tuple[str, str]]:
        items: List[Tuple[str, str]] = []
        for k, v in (data or {}).items():
            key = f"{prefix}.{k}" if prefix else str(k)
            if isinstance(v, dict):
                items.extend(self._flatten(v, key))
            else:
                try:
                    if isinstance(v, (list, tuple)):
                        value = str(v)
                    else:
                        value = str(v)
                except Exception:
                    value = repr(v)
                items.append((key, value))
        return items

    def render(self, controller, screen: pygame.Surface, *, anchor=(420, 120)):
        model = controller.model
        if not getattr(model, 'visible', False):
            self.panel_rect = None
            self.content_height = 0
            return None
        x, y = anchor
        width = 420
        height = 360
        self.panel_rect = pygame.Rect(x, y, width, height)
        surf = pygame.Surface(self.panel_rect.size, pygame.SRCALPHA)
        surf.fill((24, 24, 24, 230))
        pygame.draw.rect(surf, (100, 100, 100), surf.get_rect(), 2)
        # Header
        try:
            title_font = pygame.font.SysFont(None, 22)
            font = pygame.font.SysFont(None, 18)
            tid = None
            try:
                tid = (model.selected_template or {}).get('id')
            except Exception:
                tid = None
            header = f"Spawner Template: {tid}" if tid else "Spawner Template"
            title = title_font.render(header, True, (240, 240, 240))
            surf.blit(title, (10, 6))
            # Properties list
            y_off = 30
            rows = controller.get_rows()
            # Compute total content height (pixels)
            row_h = 20
            padding_bottom = 6
            self.content_height = len(rows) * row_h + padding_bottom
            # Visible viewport: from y_off to height-8
            viewport_top = y_off
            viewport_bottom = height - 8
            # Draw rows considering scroll_offset
            scroll = int(getattr(model, 'scroll_offset', 0) or 0)
            for i, (k, v) in enumerate(rows):
                row_y = y_off + i * row_h - scroll
                if row_y + row_h < viewport_top or row_y > viewport_bottom:
                    continue
                # Background highlight for hover / editing
                row_rect_local = pygame.Rect(6, row_y - 2, width - 12, row_h)
                if getattr(model, 'editing_row_index', None) == i:
                    draw_hover(surf, row_rect_local, color=(60, 100, 160, 120))
                elif getattr(model, 'hovered_index', None) == i:
                    draw_hover(surf, row_rect_local, color=(60, 60, 60, 80))
                key_text = font.render(str(k), True, (160, 200, 255))
                surf.blit(key_text, (10, row_y))
                # Value text or inline editor
                if getattr(model, 'editing_row_index', None) == i and controller.is_editing():
                    ti = controller.get_text_input()
                    if ti is not None:
                        ti.draw(surf, 210, row_y, color=(255, 255, 255))
                else:
                    val_text = font.render(str(v), True, (230, 230, 230))
                    surf.blit(val_text, (210, row_y))
        except Exception:
            pass
        screen.blit(surf, self.panel_rect.topleft)
        # UI blocker
        try:
            from roguelike_ui.ui_blocker import register_blocker
            register_blocker(self.panel_rect)
        except Exception:
            pass
        # Tooltip on hover (draw on screen coordinates)
        try:
            hi = getattr(model, 'hovered_index', None)
            if hi is not None:
                rows = controller.get_rows()
                if 0 <= hi < len(rows):
                    key, _ = rows[hi]
                    lines = controller.get_tooltip_lines(key)
                    mx, my = pygame.mouse.get_pos()
                    draw_tooltip(screen, mx, my, lines)
        except Exception:
            pass
        return self.panel_rect


__all__ = ["SpawnersManagerView"]
