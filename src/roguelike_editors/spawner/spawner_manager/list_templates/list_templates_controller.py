from __future__ import annotations

from typing import Optional, List, Dict, Any

from .list_templates_model import ListTemplatesModel
from .list_templates_view import ListTemplatesView
from .list_templates_events import ListTemplatesEventHandler
from roguelike_editors.spawner.services.persistence import load_spawners_json


class SpawnerTemplatesListController:
    """List controller for spawner templates (spawners.json).

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

    def render(self, screen, *, anchor=None):
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

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
                stype = sp.get('spawner_type', '?')
                trig = sp.get('trigger', {})
                ttype = trig.get('type', '?') if isinstance(trig, dict) else '?'
                items.append(f"{sid} ({stype}, trigger={ttype})")
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


__all__ = ["SpawnerTemplatesListController"]
