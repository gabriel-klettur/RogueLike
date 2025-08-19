from __future__ import annotations

from typing import Optional, List, Dict, Any

from .list_templates_model import ListTemplatesModel
from .list_templates_view import ListTemplatesView
from .list_templates_events import ListTemplatesEventHandler
from roguelike_editors.spawner.services.persistence import load_spawners_json, write_spawners_json
from .list_templates_delete.list_templates_delete_controller import ListTemplatesDeleteController
from .list_templates_delete.list_templates_delete_events import ListTemplatesDeleteEventHandler
from .list_templates_delete.list_templates_delete_model import ListTemplatesDeleteModel
from .list_templates_delete.list_templates_delete_view import ListTemplatesDeleteView


class SpawnerTemplatesListController:
    """List controller for spawner templates (spawners_templates.json).

    Uses the common list panel components via local aliases.
    """

    def __init__(self,
                 model: Optional[ListTemplatesModel] = None,
                 view: Optional[ListTemplatesView] = None) -> None:
        self.model = model or ListTemplatesModel()
        # Specific title for Templates list
        try:
            self.model.title = "Spawners Templates"
        except Exception:
            pass
        self.view = view or ListTemplatesView()
        self.events = ListTemplatesEventHandler()
        self._templates: List[Dict[str, Any]] = []
        # Optional callback set by parent (SpawnerManagerController)
        # Signature: on_add_template(template_id: str) -> None
        self.on_add_template = None  # type: ignore
        # Optional callback set by parent/editor to react after a template deletion
        # Signature: on_after_delete_template(template_id: str, removed_instances: int) -> None
        self.on_after_delete_template = None  # type: ignore
        # Delete confirmation MVC (mirrors FSM pattern)
        self.delete = ListTemplatesDeleteController()
        self.delete_events = ListTemplatesDeleteEventHandler()
        self.delete_model = ListTemplatesDeleteModel()
        self.delete_view = ListTemplatesDeleteView()

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen, controller=self)
        return self.view.render(self.model, screen, anchor=anchor, controller=self)

    def handle_event(self, event) -> bool:
        return self.events.handle_event(self, event)

    # --- Data ops ------------------------------------------------------------
    def refresh_from_disk(self) -> None:
        data = load_spawners_json()
        self._templates = data
        items: List[str] = []
        for sp in data:
            try:
                sid = sp.get('id', '?')
                # Only show template name (id)
                items.append(str(sid))
            except Exception:
                items.append(str(sp))
        self.model.items = items
        if self.model.selected_index is not None and not (0 <= self.model.selected_index < len(items)):
            self.model.selected_index = None
        # Clamp scroll window
        visible_rows = int(getattr(self.model, 'visible_rows', 11) or 11)
        max_off = max(0, len(items) - visible_rows)
        off = int(getattr(self.model, 'scroll_offset', 0) or 0)
        if off > max_off:
            self.model.scroll_offset = max_off
        if off < 0:
            self.model.scroll_offset = 0
        # Reset hover if invalid
        if self.model.hovered_index is not None and not (0 <= self.model.hovered_index < len(items)):
            self.model.hovered_index = None

    def get_selected_template(self) -> Optional[Dict[str, Any]]:
        idx = self.model.selected_index
        if idx is None:
            return None
        if 0 <= idx < len(self._templates):
            return self._templates[idx]
        return None

    # --- Row actions ---------------------------------------------------------
    def add_template_at(self, index: int) -> None:
        """Begin placement of the template on the map via parent callback."""
        try:
            if 0 <= index < len(self._templates):
                tpl = self._templates[index]
                tpl_id = str(tpl.get('id'))
                if self.on_add_template:
                    self.on_add_template(tpl_id)
        except Exception:
            pass

    def delete_template_at(self, index: int) -> None:
        """Delete the template from disk and refresh list."""
        try:
            if 0 <= index < len(self._templates):
                tpl = self._templates[index]
                tpl_id = str(tpl.get('id'))
                # Remove by id
                data = [t for t in self._templates if str(t.get('id')) != tpl_id]
                write_spawners_json(data)
                self.refresh_from_disk()
                # Adjust selection
                self.model.selected_index = None
                # Notify if someone still calls this direct path (no modal)
                try:
                    if self.on_after_delete_template:
                        self.on_after_delete_template(tpl_id, 0)
                except Exception:
                    pass
        except Exception:
            pass

    def clone_template_at(self, index: int) -> None:
        """Clone the template with a new unique id and persist to disk."""
        try:
            if 0 <= index < len(self._templates):
                orig = dict(self._templates[index])
                base_id = str(orig.get('id', 'template'))
                # Generate unique id: base-id-copy, base-id-copy-2, ...
                existing_ids = {str(t.get('id')) for t in self._templates}
                candidate = f"{base_id}-copy"
                suffix = 2
                while candidate in existing_ids:
                    candidate = f"{base_id}-copy-{suffix}"
                    suffix += 1
                orig['id'] = candidate
                # Append and write
                new_list = list(self._templates) + [orig]
                write_spawners_json(new_list)
                self.refresh_from_disk()
                # Select the new clone
                try:
                    self.model.selected_index = next((i for i, t in enumerate(self._templates) if str(t.get('id')) == candidate), None)
                except Exception:
                    pass
        except Exception:
            pass


__all__ = ["SpawnerTemplatesListController"]

