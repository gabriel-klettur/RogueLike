from __future__ import annotations

from typing import Optional

from .spawner_manager_model import SpawnerManagerModel
from .spawner_manager_view import SpawnerManagerView
from .spawner_manager_events import SpawnerManagerEventHandler
from .list_templates.list_templates_controller import SpawnerTemplatesListController
from roguelike_editors.spawner.spawner_template_properties_panel.spawners_manager_controller import (
    SpawnersManagerController as SpawnerPropertiesController,
)


class SpawnerManagerController:
    def __init__(self,
                 model: Optional[SpawnerManagerModel] = None,
                 view: Optional[SpawnerManagerView] = None) -> None:
        self.model = model or SpawnerManagerModel()
        self.view = view or SpawnerManagerView()
        self.events = SpawnerManagerEventHandler()
        # Child panels: list of templates from data/spawners/spawners_templates.json
        self.list_controller = SpawnerTemplatesListController()
        # Properties panel (shows details of selected template)
        self.props_controller = SpawnerPropertiesController()
        # Track first-time activation to refresh data
        self._was_visible = False
        # When a template is renamed from the properties panel, refresh the list and keep selection
        try:
            self.props_controller.on_template_renamed = self._handle_template_renamed
        except Exception:
            pass

    def set_visible(self, visible: bool) -> None:
        if visible and not self.model.visible:
            # Became visible -> refresh list from disk
            try:
                self.list_controller.refresh_from_disk()
            except Exception:
                pass
            # Sync selection to properties on show
            try:
                self._sync_selection_to_props()
            except Exception:
                pass
        self.model.visible = visible

    def render(self, screen, *, anchor=None):
        if not self.model.visible:
            return None
        return self.view.render(self, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        if not self.model.visible:
            return False
        return self.events.handle_event(self, event)

    # --- Internal -----------------------------------------------------------
    def _sync_selection_to_props(self) -> None:
        tpl = None
        try:
            tpl = self.list_controller.get_selected_template()
        except Exception:
            tpl = None
        try:
            self.props_controller.set_template(tpl)
        except Exception:
            pass

    # --- Callbacks -----------------------------------------------------------
    def _handle_template_renamed(self, old_id: str, new_id: str) -> None:
        """Refresh the templates list and keep selection on the renamed id."""
        try:
            # Refresh list from disk to reflect new id
            self.list_controller.refresh_from_disk()
            # Keep selection at the renamed entry
            idx = None
            try:
                idx = next((i for i, t in enumerate(self.list_controller._templates)
                            if str(t.get('id')) == str(new_id)), None)
            except Exception:
                idx = None
            self.list_controller.model.selected_index = idx
            # Sync properties panel to the (renamed) template
            self._sync_selection_to_props()
        except Exception:
            pass


__all__ = ["SpawnerManagerController"]
