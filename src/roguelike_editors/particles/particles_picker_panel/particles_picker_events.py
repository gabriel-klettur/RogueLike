import pygame
from typing import Optional

from .particles_picker_model import ParticlesPickerModel


class ParticlesPickerEventHandler:
    """Handle mouse hover and selection for the particles picker grid."""

    def __init__(self, model: ParticlesPickerModel):
        self.model = model

    def _id_at_pos(self, pos: tuple[int, int]) -> Optional[str]:
        grid = self.model.grid_rect
        if grid is None or not grid.collidepoint(pos):
            return None
        x, y = pos
        rel_x = x - grid.x
        rel_y = y - grid.y
        cell = self.model.cell_size
        margin = self.model.cell_margin
        cols = max(1, int(self.model.columns))
        stride = cell + margin
        col = rel_x // stride
        row = rel_y // stride
        # inside cell bounds?
        cx = int(col) * stride
        cy = int(row) * stride
        if rel_x - cx >= cell or rel_y - cy >= cell:
            return None  # over a margin slot
        idx = int(row) * cols + int(col)
        if idx < 0:
            return None
        keys = list(self.model.items.keys())
        if idx >= len(keys):
            return None
        return keys[idx]

    def handle(self, event: pygame.event.Event) -> bool:
        if event.type == pygame.MOUSEMOTION:
            pid = self._id_at_pos(event.pos)
            self.model.hovered_id = pid
            return False
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            pid = self._id_at_pos(event.pos)
            if pid:
                self.model.selected_id = pid
                return True
        return False
