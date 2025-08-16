from __future__ import annotations
from typing import Optional
import logging

from .fsm_sets_panel_model import FsmSetsPanelModel
from .fsm_sets_panel_view import FsmSetsPanelView
from .fsm_sets_panel_events import FsmSetsPanelEventHandler
from .sets_panel_clone.sets_panel_clone_controller import SetsPanelCloneController
from .sets_panel_clone.sets_panel_clone_events import SetsPanelCloneEventHandler
from .sets_panel_clone.sets_panel_clone_model import SetsPanelCloneModel
from .sets_panel_clone.sets_panel_clone_view import SetsPanelCloneView
from .sets_panel_delete.sets_panel_delete_controller import SetsPanelDeleteController
from .sets_panel_delete.sets_panel_delete_events import SetsPanelDeleteEventHandler
from .sets_panel_delete.sets_panel_delete_model import SetsPanelDeleteModel
from .sets_panel_delete.sets_panel_delete_view import SetsPanelDeleteView
from roguelike_editors.fsm.services.fsm_persistence import (
    default_sets_path,
    load_sets,
)

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_sets_panel.controller")


class FsmSetsPanelController:
    def __init__(self, model: Optional[FsmSetsPanelModel] = None, view: Optional[FsmSetsPanelView] = None) -> None:
        self.model = model or FsmSetsPanelModel()
        self.view = view or FsmSetsPanelView()
        self.events = FsmSetsPanelEventHandler()
        self.clone = SetsPanelCloneController()
        self.clone_events = SetsPanelCloneEventHandler()
        self.clone_model = SetsPanelCloneModel()
        self.clone_view = SetsPanelCloneView()
        self.delete = SetsPanelDeleteController()
        self.delete_events = SetsPanelDeleteEventHandler()
        # Delegated delete MVC state
        self.delete_model = SetsPanelDeleteModel()
        self.delete_view = SetsPanelDeleteView()

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen, controller=self)
        return self.view.render(self.model, screen, anchor=anchor, controller=self)

    def handle_event(self, event) -> bool:
        # Delegate all event handling to events module
        return self.events.handle_event(self, event)

    # --- Operations -----------------------------------------------------------
    def _refresh_items_from_disk(self) -> None:
        """Reload sets.json and update model.items to match current disk state."""
        try:
            data = load_sets(default_sets_path())
            set_ids = [s.get('id', '?') for s in (data.get('sets') or [])]
            self.model.items = set_ids
        except Exception as ex:
            LOGGER.exception("[SetsPanel] failed to refresh items: %s", ex)


__all__ = ["FsmSetsPanelController"]
