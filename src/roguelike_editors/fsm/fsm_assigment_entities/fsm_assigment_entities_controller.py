from __future__ import annotations
from typing import Optional, Dict, Any, List

from .fsm_assigment_entities_model import FsmAssigmentEntitiesModel, EntityAssignRow
from .fsm_assigment_entities_view import FsmAssigmentEntitiesView
from .fsm_assigment_entities_events import FsmAssigmentEntitiesEventHandler


class FsmAssigmentEntitiesController:
    def __init__(
        self,
        model: Optional[FsmAssigmentEntitiesModel] = None,
        view: Optional[FsmAssigmentEntitiesView] = None,
        events: Optional[FsmAssigmentEntitiesEventHandler] = None,
    ) -> None:
        self.model = model or FsmAssigmentEntitiesModel()
        self.view = view or FsmAssigmentEntitiesView()
        self.events = events or FsmAssigmentEntitiesEventHandler()

    # --- Data loading/refresh ---
    def _load_if_needed(self) -> None:
        if not getattr(self.model, 'needs_reload', True):
            return
        try:
            from roguelike_editors.fsm.services.fsm_persistence import (
                default_assignments_path,
                load_assignments,
            )
            path = default_assignments_path()
            try:
                data = load_assignments(path)
            except FileNotFoundError:
                data = {"by_archetype": {}, "by_eid": {}}
            by_arch = data.get("by_archetype") or {}
            by_eid = data.get("by_eid") or {}
            if not isinstance(by_arch, dict):
                by_arch = {}
            if not isinstance(by_eid, dict):
                by_eid = {}
            # Normalize string->string
            def _norm(m: Dict[str, Any]) -> Dict[str, str]:
                out = {}
                for k, v in m.items():
                    if isinstance(k, (str, int)) and isinstance(v, str):
                        out[str(k)] = v
                return out
            self.model.by_archetype = _norm(by_arch)
            self.model.by_eid = _norm(by_eid)
        except Exception:
            self.model.by_archetype = {}
            self.model.by_eid = {}
        finally:
            self.model.needs_reload = False

    def _build_rows(self) -> None:
        rows: List[EntityAssignRow] = []
        cat = getattr(self.model, 'target_category', 'by_archetype')
        data = self.model.by_archetype if cat == 'by_archetype' else self.model.by_eid
        for k in sorted(data.keys(), key=lambda s: str(s)):
            rows.append(EntityAssignRow(key=str(k), value=data.get(k)))
        self.model.rows = rows

    # --- Public API ---
    def render(self, screen, *, anchor=None):
        if not getattr(self.model, 'visible', False):
            return None
        self._load_if_needed()
        self._build_rows()
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        return self.events.handle_event(self, event)

    # --- Helpers for events ---
    def _navigate_category(self, step: int) -> None:
        cats = ["by_archetype", "by_eid"]
        try:
            idx = cats.index(self.model.target_category)
        except Exception:
            idx = 0
        idx = (idx + int(step)) % len(cats)
        self.model.target_category = cats[idx]
        self.model.selected_index = None
        self.model.hovered_index = None
        self.model.editing_index = None
        self.model.editing_text = ""
        self._build_rows()

    def _commit_edit(self) -> None:
        idx = self.model.editing_index
        if idx is None or not (0 <= int(idx) < len(self.model.rows)):
            self.model.editing_index = None
            self.model.editing_text = ""
            return
        row = self.model.rows[int(idx)]
        txt = (self.model.editing_text or "").strip()
        cat = getattr(self.model, 'target_category', 'by_archetype')
        mapping = self.model.by_archetype if cat == 'by_archetype' else self.model.by_eid
        # Apply change
        if txt:
            mapping[row.key] = txt
        else:
            mapping.pop(row.key, None)
        # Save
        try:
            from roguelike_editors.fsm.services.fsm_persistence import (
                default_assignments_path,
                save_assignments,
            )
            data = {
                "by_archetype": dict(sorted(self.model.by_archetype.items())),
                "by_eid": dict(sorted(self.model.by_eid.items(), key=lambda kv: str(kv[0]))),
            }
            save_assignments(data, default_assignments_path())
        except Exception:
            pass
        self.model.editing_index = None
        self.model.editing_text = ""
        self._build_rows()


__all__ = ["FsmAssigmentEntitiesController"]

