from __future__ import annotations

from typing import Any
import pygame


class ItemsModesService:
    """Gestiona modos de spawn/delete y la orquestación UI relacionada."""

    def __init__(self, controller: Any) -> None:
        self.c = controller

    def enter_spawn_mode(self) -> None:
        if self.c.model.delete_mode_active:
            self.exit_delete_mode()
        self.c.model.spawn_mode_active = True
        self.c.model.spawn_item_id = None
        self.c.picker_controller.model.visible = True

    def exit_spawn_mode(self) -> None:
        self.c.model.spawn_mode_active = False
        self.c.model.spawn_item_id = None
        try:
            pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW)
        except Exception:
            pass

    def enter_delete_mode(self) -> None:
        if self.c.model.spawn_mode_active:
            self.exit_spawn_mode()
        self.c.model.delete_mode_active = True
        try:
            self.c.picker_controller.model.visible = True
        except Exception:
            pass
        try:
            pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_CROSSHAIR)
        except Exception:
            pass

    def exit_delete_mode(self) -> None:
        self.c.model.delete_mode_active = False
        try:
            pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW)
        except Exception:
            pass

    def enter_add_items_on_system_mode(self) -> None:
        try:
            self.c.picker_controller.model.visible = False
        except Exception:
            pass
        try:
            pp_model = self.c.properties_controller.model
            setattr(pp_model, 'expand_into_picker_space', True)
        except Exception:
            pass

    def exit_add_items_on_system_mode(self) -> None:
        try:
            self.c.picker_controller.model.visible = True
        except Exception:
            pass
        try:
            pp_model = self.c.properties_controller.model
            setattr(pp_model, 'expand_into_picker_space', False)
        except Exception:
            pass
