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
try:
    # Optional: read hovered set context (id+params) from runtime and lint it
    from roguelike_editors.fsm.services.fsm_runtime_bridge import (
        get_editor_highlight_context as _fsm_get_highlight_ctx,
        lint_set_params as _fsm_lint,
    )
except Exception:  # pragma: no cover - service may not be present in some contexts
    _fsm_get_highlight_ctx = None
    _fsm_lint = None

# Optional: ids index helpers (may not be present in some contexts)
try:  # pragma: no cover
    from roguelike_editors.fsm.services.fsm_runtime_bridge import get_set_ids as _get_set_ids
except Exception:  # pragma: no cover
    _get_set_ids = None

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
        # Sync hover highlight from runtime (if any)
        if getattr(self.model, 'visible', False) and _fsm_get_highlight_ctx is not None:
            try:
                hid, params = _fsm_get_highlight_ctx()
                self.model.highlighted_set_id = hid
                # Compute warnings if we have a linter
                if _fsm_lint is not None and hid:
                    try:
                        self.model.highlighted_warnings = list(_fsm_lint(hid, params) or [])
                    except Exception:
                        self.model.highlighted_warnings = []
                else:
                    self.model.highlighted_warnings = []
                # Reflect as hovered row
                if hid:
                    items = getattr(self.model, 'items', [])
                    try:
                        self.model.hovered_index = items.index(hid)
                    except ValueError:
                        self.model.hovered_index = None
                else:
                    self.model.hovered_index = None
            except Exception:
                pass
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
            # 1) Priorizar lectura directa desde disco (permite a los tests monkeypatchear load_sets/default_sets_path)
            try:
                data = load_sets(default_sets_path())
            except Exception:
                data = {}
            set_ids = [s.get('id', '?') for s in (data.get('sets') or [])]
            # 2) Si está vacío, caer al helper rápido del runtime bridge
            if not set_ids and _get_set_ids is not None:
                try:
                    set_ids = list(_get_set_ids() or [])
                except Exception:
                    set_ids = []
            self.model.items = set_ids
        except Exception as ex:
            LOGGER.exception("[SetsPanel] failed to refresh items: %s", ex)


__all__ = ["FsmSetsPanelController"]
