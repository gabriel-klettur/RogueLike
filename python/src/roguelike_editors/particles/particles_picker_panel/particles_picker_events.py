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
        """Hit-test using precomputed cell_rects for robust grouped/flat layouts."""
        if not isinstance(self.model.cell_rects, dict) or not self.model.cell_rects:
            return None
        for pid, rect in self.model.cell_rects.items():
            try:
                if rect.collidepoint(pos):
                    return str(pid)
            except Exception:
                continue
        return None

    def _toggle_hit(self, pos: tuple[int, int]) -> bool:
        rect = getattr(self.model, 'toggle_rect', None)
        try:
            return rect is not None and rect.collidepoint(pos)
        except Exception:
            return False

    def handle(self, event: pygame.event.Event) -> bool:
        # Scroll helpers
        def _can_scroll_at(pos: tuple[int, int]) -> bool:
            rect = getattr(self.model, 'grid_rect', None)
            try:
                return rect is not None and rect.collidepoint(pos)
            except Exception:
                return False

        def _scroll_by(dy_px: int) -> None:
            try:
                sy = int(getattr(self.model, 'scroll_y', 0)) + int(dy_px)
                max_scroll = max(0, int(getattr(self.model, 'content_height', 0)) - int(getattr(self.model, 'viewport_height', 0)))
                if sy < 0:
                    sy = 0
                if sy > max_scroll:
                    sy = max_scroll
                self.model.scroll_y = int(sy)
            except Exception:
                pass

        # Mouse wheel (new pygame event)
        if event.type == pygame.MOUSEWHEEL:
            # Positive y means wheel up (scroll content down -> decrease scroll_y)
            step = 48
            if _can_scroll_at(pygame.mouse.get_pos()):
                _scroll_by(int(-event.y * step))
                return True

        # Legacy wheel buttons (4=up,5=down)
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) in (4, 5):
            step = 48
            if _can_scroll_at(getattr(event, 'pos', pygame.mouse.get_pos())):
                if event.button == 4:
                    _scroll_by(-step)
                else:
                    _scroll_by(step)
                return True

        if event.type == pygame.MOUSEMOTION:
            pid = self._id_at_pos(event.pos)
            self.model.hovered_id = pid
            return False
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            # Grouping toggle button
            if self._toggle_hit(event.pos):
                try:
                    self.model.group_by_kind = not bool(getattr(self.model, 'group_by_kind', False))
                except Exception:
                    self.model.group_by_kind = True
                # No need to rebuild providers; layout will refresh on next draw
                return True
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
        # Right-click drag placement start: begin placing selected preset directly from picker
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            pid = self._id_at_pos(event.pos)
            if pid:
                # Resolve editor controller and set drag state on editor model
                try:
                    editor = getattr(self.controller, 'editor_controller', None)
                    if editor is not None and hasattr(editor, 'model'):
                        editor.model.drag_place_active = True
                        editor.model.drag_pid = str(pid)
                        editor.model.drag_entity_eid = None
                        # Ensure picker isn't blinking add-mode
                        try:
                            self.model.add_mode_active = False
                        except Exception:
                            pass
                        return True
                except Exception:
                    pass
        return False
