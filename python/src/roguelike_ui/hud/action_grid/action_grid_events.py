from __future__ import annotations

import pygame

from .action_grid_model import ActionGridModel
from roguelike_engine.config.config_hud import (
    GRID_MARGIN,
    GRID_PADDING,
    GRID_CELL_SIZE,
    GRID_BOTTOM_MARGIN,
    MINIMIZE_BUTTON_SIZE,
    MINIMIZED_BOX_SIZE,
)


class ActionGridEvents:
    def _compute_rects(self, screen: pygame.Surface, model: ActionGridModel):
        cw, ch = GRID_CELL_SIZE
        cols, rows = model.cols, model.rows
        grid_w = cols * cw + (cols - 1) * GRID_MARGIN + GRID_PADDING * 2
        grid_h = rows * ch + (rows - 1) * GRID_MARGIN + GRID_PADDING * 2
        sw, sh = screen.get_size()
        start_x = (sw - grid_w) // 2
        start_y = sh - grid_h - GRID_BOTTOM_MARGIN
        rects: list[pygame.Rect] = []
        for r in range(rows):
            for c in range(cols):
                x = start_x + GRID_PADDING + c * (cw + GRID_MARGIN)
                y = start_y + GRID_PADDING + r * (ch + GRID_MARGIN)
                rects.append(pygame.Rect(x, y, cw, ch))
        return start_x, start_y, grid_w, grid_h, rects

    def handle_event(self, event: pygame.event.Event, model: ActionGridModel) -> bool:
        try:
            screen = pygame.display.get_surface()
            if screen is None:
                return False
            start_x, start_y, grid_w, grid_h, rects = self._compute_rects(screen, model)
            total = model.rows * model.cols
            if total < 2:
                return False
            prev_idx = total - 2
            next_idx = total - 1
            if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
                pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
                # Minimized box click -> maximize
                if getattr(model, 'minimized', False):
                    mw, mh = MINIMIZED_BOX_SIZE
                    bx = (screen.get_width() - mw) // 2
                    by = screen.get_height() - mh - GRID_BOTTOM_MARGIN
                    if pygame.Rect(bx, by, mw, mh).collidepoint(pos):
                        model.minimized = False
                        return True
                    return False
                # Minimize button when expanded
                btn_w, btn_h = MINIMIZE_BUTTON_SIZE
                btn_x = start_x + grid_w - GRID_PADDING - btn_w
                btn_y = start_y + GRID_PADDING - 2
                if pygame.Rect(btn_x, btn_y, btn_w, btn_h).collidepoint(pos):
                    model.minimized = True
                    return True
                if rects[prev_idx].collidepoint(pos):
                    model.page = max(0, model.page - 1)
                    return True
                if rects[next_idx].collidepoint(pos):
                    max_page = max(0, model.pages() - 1)
                    model.page = min(max_page, model.page + 1)
                    return True
        except Exception:
            return False
        return False
