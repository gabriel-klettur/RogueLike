"""Picker UI interactions for the Buildings Colliders panel."""
from __future__ import annotations

from typing import Any, Iterable

import pygame


class PickerController:
    """Dispatch picker-related mouse interactions."""

    def __init__(self, editor_state: Any, model: Any, persistence: Any, logger: Any) -> None:
        self.editor_state = editor_state
        self.model = model
        self.persistence = persistence
        self.logger = logger

    # ------------------------------------------------------------------
    # Event entry points
    # ------------------------------------------------------------------
    def handle_mouse_down(self, event: pygame.event.Event, buildings: Iterable[Any]) -> bool:
        if not getattr(self.model, "picker_open", False):
            return False

        mouse_x, mouse_y = event.pos
        picker_x, picker_y = self.model.picker_pos or (0, 0)
        width, height = self.model.picker_panel_size
        if not (picker_x <= mouse_x <= picker_x + width and picker_y <= mouse_y <= picker_y + height):
            return False

        if event.button == 1:
            if self._handle_save_button(mouse_x, mouse_y, buildings):
                return True
            return self._handle_choice_selection(mouse_x, mouse_y)
        if event.button == 3:
            self._start_picker_drag(mouse_x, mouse_y, picker_x, picker_y)
            return True
        return False

    def handle_mouse_up(self, event: pygame.event.Event) -> bool:
        if event.button == 3 and getattr(self.model, "picker_dragging", False):
            self.model.picker_dragging = False
            return True
        return False

    def handle_mouse_motion(self, event: pygame.event.Event) -> bool:
        if getattr(self.model, "picker_dragging", False):
            dx, dy = self.model.picker_drag_offset
            self.model.picker_pos = (event.pos[0] - dx, event.pos[1] - dy)
            self._mark_picker_moved()
            return True
        return False

    # ------------------------------------------------------------------
    # Picker helpers
    # ------------------------------------------------------------------
    def _handle_save_button(self, mouse_x: int, mouse_y: int, buildings: Iterable[Any]) -> bool:
        save_rect = self.model.picker_rects.get("save_cu")
        if save_rect and save_rect.collidepoint((mouse_x, mouse_y)):
            previous_scope = getattr(self.editor_state, "collider_scope", "CG")
            try:
                self.editor_state.collider_scope = "CU"
                self.persistence.save(buildings)
            finally:
                try:
                    self.editor_state.collider_scope = previous_scope
                except Exception:  # pragma: no cover - defensive guard
                    pass
            try:
                self.logger.info(
                    "[Colliders][CU] Guardado per-instance en buildings_collisions_by_building_instance_id.json"
                )
            except Exception:  # pragma: no cover
                pass
            self._mark_tutorial_saved()
            return True
        return False

    def _handle_choice_selection(self, mouse_x: int, mouse_y: int) -> bool:
        for key, rect in self.model.picker_rects.items():
            if key == "save_cu":
                continue
            if rect.collidepoint((mouse_x, mouse_y)):
                self.model.choice = key
                self._mark_tutorial_choice()
                self._log_choice(key)
                return True
        return False

    def _start_picker_drag(self, mouse_x: int, mouse_y: int, picker_x: int, picker_y: int) -> None:
        self.model.picker_dragging = True
        self.model.picker_drag_offset = (mouse_x - picker_x, mouse_y - picker_y)

    # ------------------------------------------------------------------
    # Tutorial and logging helpers
    # ------------------------------------------------------------------
    def _mark_tutorial_saved(self) -> None:
        try:
            setattr(self.editor_state, "tutorial_colliders_saved_button_pulse", True)
        except Exception:  # pragma: no cover
            pass

    def _mark_tutorial_choice(self) -> None:
        try:
            setattr(self.editor_state, "tutorial_colliders_choice_pulse", True)
        except Exception:  # pragma: no cover
            pass

    def _mark_picker_moved(self) -> None:
        try:
            setattr(self.editor_state, "tutorial_colliders_picker_moved_pulse", True)
        except Exception:  # pragma: no cover
            pass

    def _log_choice(self, key: str) -> None:
        try:
            self.logger.info(f"[Colliders] Seleccionado tipo '{key}' en el picker")
        except Exception:  # pragma: no cover
            pass
