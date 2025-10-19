from __future__ import annotations

import logging
from types import SimpleNamespace
from typing import Any


class ItemsEditorCallbacks:
    """Callbacks del Items Editor extraídos desde el controlador para claridad y reuso."""

    def __init__(self, controller: Any) -> None:
        self.c = controller

    def set_selected_item(self, item_id: str) -> None:
        try:
            if getattr(self.c.items_add_remove_model, 'active_tool', None) == 'remove_item' or \
               getattr(self.c.model, 'delete_mode_active', False):
                ok = self.c.delete_item_from_system(item_id)
                if ok:
                    logging.getLogger(__name__).info("[ItemsEditorController] Deleted item '%s' from system via picker", item_id)
                    self.c.model.selected_item_id = None
                    self.c.picker_controller.model.selected_item_id = None
                    self.c.properties_controller.update_context(self.c.model.items, None, self.c.model.hovered_item_id)
                    try:
                        self.c.instances_controller.reload_data()
                    except Exception:
                        pass
                    return
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] remove_item via picker failed")
        self.c.model.selected_item_id = item_id
        self.c.picker_controller.model.selected_item_id = item_id
        self.c.properties_controller.update_context(self.c.model.items, self.c.model.selected_item_id, self.c.model.hovered_item_id)
        try:
            setattr(self.c.model, 'tutorial_spawn_selection_pulse', True)
        except Exception:
            pass

    def on_open_id(self, item_id: str) -> None:
        try:
            if getattr(self.c.items_add_remove_model, 'active_tool', None) == 'remove_item' or \
               getattr(self.c.model, 'delete_mode_active', False):
                ok = self.c.delete_item_from_system(item_id)
                if ok:
                    logging.getLogger(__name__).info("[ItemsEditorController] Deleted item '%s' from system via open", item_id)
                    self.c.model.selected_item_id = None
                    self.c.picker_controller.model.selected_item_id = None
                    self.c.properties_controller.update_context(self.c.model.items, None, self.c.model.hovered_item_id)
                    try:
                        self.c.instances_controller.reload_data()
                    except Exception:
                        pass
                    return
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] remove_item via open failed")
        self.set_selected_item(item_id)
        self.c.properties_controller.update_context(self.c.model.items, self.c.model.selected_item_id, self.c.model.hovered_item_id)
        self.c.properties_controller.start_inline_edit()

    def start_hold_focus(self, x: float, y: float) -> None:
        if not hasattr(self.c, 'game'):
            return
        try:
            logging.getLogger(__name__).info("[ItemsEditorController] Focusing camera at (%.2f, %.2f)", x, y)
            self.c.game.camera.update(SimpleNamespace(x=x, y=y))
            self.c.model.holding_pos_focus = True
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] start_hold_focus failed")

    def end_hold_focus(self) -> None:
        if not hasattr(self.c, 'game'):
            self.c.model.holding_pos_focus = False
            return
        try:
            pos = getattr(self.c.game.ecs.ecs_world, 'player_position', None)
            if pos is not None:
                logging.getLogger(__name__).info("[ItemsEditorController] Restoring camera to player at (%.2f, %.2f)", pos.x, pos.y)
                self.c.game.camera.update(SimpleNamespace(x=pos.x, y=pos.y))
            self.c.model.holding_pos_focus = False
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] end_hold_focus failed")
