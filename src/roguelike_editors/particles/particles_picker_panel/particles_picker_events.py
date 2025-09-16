import pygame
from typing import Optional

from .particles_picker_model import ParticlesPickerModel
from roguelike_ui.services.json_persistence import remove_from_json
from roguelike_game.config.particles_config import reload_particles
import os


class ParticlesPickerEventHandler:
    """Handle mouse hover and selection for the particles picker grid."""

    def __init__(self, model: ParticlesPickerModel, controller=None):
        self.model = model
        # Optional reference to ParticlesPickerController for rebuild()
        # and editor_controller back-reference if provided by parent editor.
        self.controller = controller

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
                # Deletion flow when delete mode is active
                if getattr(self.model, 'delete_mode_active', False):
                    # Remove from particles JSON and rebuild catalog/picker
                    path = os.path.join(os.getcwd(), "data", "particles", "particles.json")
                    removed = False
                    try:
                        removed = remove_from_json(path, pid)
                    except Exception:
                        removed = False
                    if removed:
                        try:
                            reload_particles()
                        except Exception:
                            pass
                        # Rebuild picker items from catalog
                        try:
                            if self.controller is not None and hasattr(self.controller, 'rebuild'):
                                self.controller.rebuild()
                        except Exception:
                            pass
                    # Exit delete mode and clear selection
                    self.model.delete_mode_active = False
                    self.model.selected_id = None
                    # Also reset editor flags and AR panel if reachable
                    try:
                        editor = getattr(self.controller, 'editor_controller', None)
                        if editor is not None:
                            editor.model.delete_mode_active = False
                            ar = getattr(editor, 'particles_add_remove_model', None)
                            if ar is not None:
                                ar.active_tool = None
                    except Exception:
                        pass
                    return True
                # Normal selection flow
                self.model.selected_id = pid
                return True
        return False
