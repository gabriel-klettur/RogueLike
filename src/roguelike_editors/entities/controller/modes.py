from __future__ import annotations
import pygame
from roguelike_editors.entities.services.ui_helpers import hide_assets_picker_and_clear_properties


class EditorModes:
    """Encapsula cambios de modo del editor (spawn/delete/add-on-system)."""

    def __init__(self, editor: "EntitiesEditorController") -> None:
        self.editor = editor

    def enter_spawn_mode(self, entity_type: str | None = None) -> None:
        editor = self.editor
        if editor.model.delete_mode_active:
            self.exit_delete_mode()
        editor.model.spawn_mode_active = True
        editor.model.spawn_entity_type = entity_type
        editor.picker_controller.model.blink = True
        editor.picker_controller.model.visible = True
        editor.picker_controller.model.selected_id = None
        hide_assets_picker_and_clear_properties(editor.properties_controller)

    def exit_spawn_mode(self) -> None:
        editor = self.editor
        editor.model.spawn_mode_active = False
        editor.model.spawn_entity_type = None
        editor.picker_controller.model.blink = False
        editor.picker_controller.model.selection_blink = False
        pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW)

    def enter_delete_mode(self) -> None:
        editor = self.editor
        if editor.model.spawn_mode_active:
            self.exit_spawn_mode()
        editor.model.delete_mode_active = True
        pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_CROSSHAIR)
        hide_assets_picker_and_clear_properties(editor.properties_controller)

    def exit_delete_mode(self) -> None:
        editor = self.editor
        editor.model.delete_mode_active = False
        pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW)

    def enter_add_entities_on_system_mode(self) -> None:
        editor = self.editor
        editor.picker_controller.model.visible = False
        pp_model = editor.properties_controller.model
        pp_view = editor.properties_controller.view
        if getattr(pp_model, 'saved_drag_pos', None) is None:
            pp_model.saved_drag_pos = pp_view.draggable_panel.pos
        left_x = editor.picker_controller.view.x
        top_y = editor.picker_controller.view.y
        pp_model.expand_into_picker_space = True
        pp_model.panel_left_x_override = left_x
        pp_view.draggable_panel.pos = (left_x, top_y)

    def exit_add_entities_on_system_mode(self) -> None:
        editor = self.editor
        editor.picker_controller.model.visible = True
        pp_model = editor.properties_controller.model
        pp_view = editor.properties_controller.view
        pp_model.expand_into_picker_space = False
        pp_model.panel_left_x_override = None
        if getattr(pp_model, 'saved_drag_pos', None) is not None:
            pp_view.draggable_panel.pos = pp_model.saved_drag_pos
            pp_model.saved_drag_pos = None
