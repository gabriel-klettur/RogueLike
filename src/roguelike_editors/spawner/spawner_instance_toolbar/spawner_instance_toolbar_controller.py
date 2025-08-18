from __future__ import annotations

from typing import Optional
import logging

from .spawner_instance_toolbar_model import SpawnerInstanceToolbarModel
from .spawner_instance_toolbar_view import SpawnerInstanceToolbarView
from .spawner_instance_toolbar_events import SpawnerInstanceToolbarEventHandler
from roguelike_editors.spawner.services.persistence import load_instances_json, write_instances_json


class SpawnerInstanceToolbarController:
    def __init__(self,
                 editor_controller,
                 model: Optional[SpawnerInstanceToolbarModel] = None,
                 view: Optional[SpawnerInstanceToolbarView] = None,
                 events: Optional[SpawnerInstanceToolbarEventHandler] = None) -> None:
        self.editor_controller = editor_controller
        self.model = model or SpawnerInstanceToolbarModel()
        self.view = view or SpawnerInstanceToolbarView()
        self.events = events or SpawnerInstanceToolbarEventHandler()

    def render(self, screen, *, anchor=None):
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        consumed = False
        # Ensure toolbar is constructed for hit-testing
        try:
            ensure = getattr(self.view, 'ensure_ready', None)
            if ensure:
                ensure(self.model)
        except Exception:
            pass
        toolbar = getattr(self.view, 'toolbar', None)
        if toolbar is not None:
            try:
                consumed = bool(toolbar.handle_event(event)) or consumed
            except Exception:
                pass
        consumed = self.events.handle_event(self, event) or consumed
        return consumed

    # Actions -----------------------------------------------------------------
    def on_add_spawner(self) -> None:
        """Begin placement using the selected instance's template_id if available;
        otherwise open the Templates Manager to pick one.
        """
        try:
            inst = self.editor_controller.spawner_instances.get_selected_instance()
            tpl_id = inst.get('template_id') if inst else None
        except Exception:
            tpl_id = None
        if tpl_id:
            try:
                self.editor_controller._begin_place_template(str(tpl_id))
            except Exception:
                logging.getLogger(__name__).debug("[InstanceToolbar] _begin_place_template failed", exc_info=False)
        else:
            # No selection: switch to templates manager tool to let user choose
            try:
                tb = getattr(self.editor_controller, 'spawner_toolbar', None)
                if tb and getattr(tb, 'model', None) is not None:
                    tb.model.active_tool = 'spawner_manager'
            except Exception:
                pass

    def on_remove_spawner(self) -> None:
        """Delete the currently selected instance from instances.json and refresh the list."""
        try:
            inst = self.editor_controller.spawner_instances.get_selected_instance()
        except Exception:
            inst = None
        if not inst:
            return
        try:
            target_id = str(inst.get('id')) if inst.get('id') is not None else None
        except Exception:
            target_id = None
        data = load_instances_json()
        changed = False
        if target_id:
            new_data = [x for x in data if str(x.get('id')) != target_id]
            changed = len(new_data) != len(data)
            data = new_data
        else:
            # Fallback: match by tuple(template_id, zone, tile)
            try:
                key = (inst.get('template_id'), inst.get('zone'), tuple(inst.get('tile', [0, 0])))
                def _same(x):
                    return (x.get('template_id'), x.get('zone'), tuple(x.get('tile', [0, 0]))) == key
                before = len(data)
                data = [x for x in data if not _same(x)]
                changed = len(data) != before
            except Exception:
                changed = False
        if changed:
            try:
                write_instances_json(data)
            except Exception:
                logging.getLogger(__name__).warning("[InstanceToolbar] Failed to write instances.json", exc_info=False)
            # Refresh list and hide properties if nothing selected
            try:
                self.editor_controller.spawner_instances.refresh_from_disk()
            except Exception:
                pass
            try:
                self.editor_controller.instance_properties.model.visible = False
            except Exception:
                pass


__all__ = ["SpawnerInstanceToolbarController"]
