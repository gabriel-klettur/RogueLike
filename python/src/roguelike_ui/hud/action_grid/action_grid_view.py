from __future__ import annotations

from typing import Tuple, List
import pygame

from .action_grid_model import ActionGridModel
from roguelike_engine.config.config_hud import (
    GRID_ROWS,
    GRID_COLS,
    GRID_CELL_SIZE,
    GRID_MARGIN,
    GRID_PADDING,
    GRID_BOTTOM_MARGIN,
    COLOR_BG,
    COLOR_BORDER,
    COLOR_TEXT,
    COLOR_PAGING_HOVER,
    MINIMIZE_BUTTON_SIZE,
    MINIMIZED_BOX_SIZE,
)


class ActionGridView:
    """Draws the Action Grid (10x3) with labels and binding hints."""

    def __init__(self, cell_size: Tuple[int, int] | None = None) -> None:
        self.cell_size = cell_size or GRID_CELL_SIZE
        try:
            self.font = pygame.font.SysFont("consolas", 16)
        except Exception:
            self.font = pygame.font.Font(None, 16)

    def _visible_items(self, model: ActionGridModel) -> List[str]:
        # Reserve last two cells for paging controls
        total = model.rows * model.cols
        visible_slots = max(0, total - 2)
        start = model.page * visible_slots
        end = start + visible_slots
        return model.items[start:end]

    def _compute_layout(self, screen: pygame.Surface, model: ActionGridModel):
        cw, ch = self.cell_size
        cols, rows = model.cols, model.rows
        grid_w = cols * cw + (cols - 1) * GRID_MARGIN + GRID_PADDING * 2
        grid_h = rows * ch + (rows - 1) * GRID_MARGIN + GRID_PADDING * 2
        sw, sh = screen.get_size()
        # Bottom-center anchor with small bottom margin
        start_x = (sw - grid_w) // 2
        start_y = sh - grid_h - GRID_BOTTOM_MARGIN
        # Build screen-space rects per cell
        rects: list[pygame.Rect] = []
        for r in range(rows):
            for c in range(cols):
                x = start_x + GRID_PADDING + c * (cw + GRID_MARGIN)
                y = start_y + GRID_PADDING + r * (ch + GRID_MARGIN)
                rects.append(pygame.Rect(x, y, cw, ch))
        return (start_x, start_y, grid_w, grid_h, rects)

    def render(self, screen: pygame.Surface, model: ActionGridModel, *, get_binding_label) -> None:
        cw, ch = self.cell_size
        cols, rows = model.cols, model.rows
        sw, sh = screen.get_size()

        # If minimized, render compact box with maximize control
        if getattr(model, 'minimized', False):
            mw, mh = MINIMIZED_BOX_SIZE
            bx = (sw - mw) // 2
            by = sh - mh - GRID_BOTTOM_MARGIN
            mini = pygame.Surface((mw, mh), pygame.SRCALPHA)
            mini.fill(COLOR_BG)
            pygame.draw.rect(mini, COLOR_BORDER, mini.get_rect(), width=1)
            # Label
            title = self.font.render("Grid [+]", True, COLOR_TEXT[:3])
            mini.blit(title, ((mw - title.get_width()) // 2, (mh - title.get_height()) // 2))
            # Hover highlight over entire minimized box
            if pygame.Rect(bx, by, mw, mh).collidepoint(pygame.mouse.get_pos()):
                s = pygame.Surface((mw, mh), pygame.SRCALPHA)
                s.fill(COLOR_PAGING_HOVER)
                mini.blit(s, (0, 0))
            screen.blit(mini, (bx, by))
            return

        # Compute layout when not minimized
        start_x, start_y, grid_w, grid_h, rects = self._compute_layout(screen, model)

        # Background box
        box = pygame.Surface((grid_w, grid_h), pygame.SRCALPHA)
        box.fill(COLOR_BG)
        pygame.draw.rect(box, COLOR_BORDER, box.get_rect(), width=1)

        # Minimize button (top-right inside the box)
        btn_w, btn_h = MINIMIZE_BUTTON_SIZE
        btn_x = grid_w - GRID_PADDING - btn_w
        btn_y = GRID_PADDING - 2
        min_rect_box_space = pygame.Rect(btn_x, btn_y, btn_w, btn_h)
        # Hover highlight
        mouse_pos = pygame.mouse.get_pos()
        if pygame.Rect(start_x + btn_x, start_y + btn_y, btn_w, btn_h).collidepoint(mouse_pos):
            s_btn = pygame.Surface((btn_w, btn_h), pygame.SRCALPHA)
            s_btn.fill(COLOR_PAGING_HOVER)
            box.blit(s_btn, (btn_x, btn_y))
        # Draw button border and symbol
        pygame.draw.rect(box, COLOR_BORDER, min_rect_box_space, width=1)
        sym = self.font.render("-", True, COLOR_TEXT[:3])
        box.blit(sym, (btn_x + (btn_w - sym.get_width()) // 2, btn_y + (btn_h - sym.get_height()) // 2))

        # Draw cells
        items = self._visible_items(model)
        total_cells = rows * cols
        prev_idx = total_cells - 2
        next_idx = total_cells - 1

        # Draw cells relative to box surface coordinates
        for i, cell_rect_screen in enumerate(rects):
            # Translate to box-local coordinates
            x = cell_rect_screen.x - start_x
            y = cell_rect_screen.y - start_y
            local_rect = pygame.Rect(x, y, cw, ch)

            # Hover highlight for any interactive cell (items or paging)
            is_item = i < len(items)
            is_paging = i in (prev_idx, next_idx)
            if (is_item or is_paging) and cell_rect_screen.collidepoint(mouse_pos):
                s = pygame.Surface((cw, ch), pygame.SRCALPHA)
                s.fill(COLOR_PAGING_HOVER)
                box.blit(s, (x, y))

            # Border
            pygame.draw.rect(box, COLOR_BORDER, local_rect, width=1)

            # Content: items or paging labels in last two cells
            if i < len(items):
                action = items[i]
                title = action[:12]
                t_surf = self.font.render(title, True, COLOR_TEXT[:3])
                box.blit(t_surf, (x + 4, y + 4))
                hint = get_binding_label(action)
                if hint:
                    h_surf = self.font.render(hint, True, COLOR_TEXT[:3])
                    box.blit(h_surf, (x + 4, y + ch - h_surf.get_height() - 4))
            elif i == prev_idx:
                t_surf = self.font.render("Prev", True, COLOR_TEXT[:3])
                # Center label
                box.blit(t_surf, (x + (cw - t_surf.get_width()) // 2, y + (ch - t_surf.get_height()) // 2))
            elif i == next_idx:
                t_surf = self.font.render("Next", True, COLOR_TEXT[:3])
                box.blit(t_surf, (x + (cw - t_surf.get_width()) // 2, y + (ch - t_surf.get_height()) // 2))

        screen.blit(box, (start_x, start_y))
