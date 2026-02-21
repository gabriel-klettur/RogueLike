from __future__ import annotations
from typing import Optional, Dict, Any, List, Set

from .fsm_assigment_animations_model import FsmAssigmentAnimationsModel, AnimRow
from .fsm_assigment_animations_view import FsmAssigmentAnimationsView
from .fsm_assigment_animations_events import FsmAssigmentAnimationsEventHandler

# Optional ids index helper (may not be present in some contexts)
try:  # pragma: no cover
    from roguelike_editors.fsm.services.fsm_runtime_bridge import get_set_ids as _get_set_ids
except Exception:  # pragma: no cover
    _get_set_ids = None


class FsmAssigmentAnimationsController:
    def __init__(
        self,
        model: Optional[FsmAssigmentAnimationsModel] = None,
        view: Optional[FsmAssigmentAnimationsView] = None,
        events: Optional[FsmAssigmentAnimationsEventHandler] = None,
    ) -> None:
        self.model = model or FsmAssigmentAnimationsModel()
        self.view = view or FsmAssigmentAnimationsView()
        self.events = events or FsmAssigmentAnimationsEventHandler()

    # --- Data loading/refresh ---
    def _load_if_needed(self) -> None:
        if not getattr(self.model, 'needs_reload', True):
            return
        try:
            from roguelike_editors.fsm.services.fsm_persistence import (
                default_animation_map_path,
                load_animation_map,
            )
            path = default_animation_map_path()
            try:
                data = load_animation_map(path)
            except FileNotFoundError:
                data = {"default": {}, "overrides": {}}
            default = data.get("default") or {}
            overrides = data.get("overrides") or {}
            if not isinstance(default, dict):
                default = {}
            if not isinstance(overrides, dict):
                overrides = {}
            # Normalize string values
            def _norm_map(m: Dict[str, Any]) -> Dict[str, str]:
                out = {}
                for k, v in m.items():
                    if isinstance(k, str) and isinstance(v, str):
                        out[k] = v
                return out
            self.model.default_map = _norm_map(default)
            by_set: Dict[str, Dict[str, str]] = {}
            for sid, mm in overrides.items():
                if not isinstance(sid, str) or not isinstance(mm, dict):
                    continue
                by_set[sid] = _norm_map(mm)
            self.model.overrides_map = by_set
        except Exception:
            # Fallback to empty maps on any error
            self.model.default_map = {}
            self.model.overrides_map = {}
        finally:
            self.model.needs_reload = False

    def _refresh_targets(self) -> None:
        # Build list of available targets: 'default' + all set ids
        set_ids: List[str] = []
        if _get_set_ids is not None:
            try:
                set_ids = list(_get_set_ids() or [])
            except Exception:
                set_ids = []
        if not set_ids:
            try:
                from roguelike_editors.fsm.services.fsm_runtime_bridge import get_snapshot
                snap = get_snapshot()
                set_ids = [s.get('id') for s in snap.get('sets', []) if isinstance(s.get('id'), str)]
            except Exception:
                set_ids = []
        targets = ['default'] + sorted({sid for sid in set_ids if isinstance(sid, str)})
        self.model.available_targets = targets
        if self.model.target_set_id not in targets:
            self.model.target_set_id = 'default'

    def _collect_all_state_classes(self) -> List[str]:
        # Union of classes from sets snapshot and existing animation map keys
        classes: Set[str] = set()
        try:
            from roguelike_editors.fsm.services.fsm_runtime_bridge import get_snapshot
            snap = get_snapshot()
            for s in snap.get('sets', []) or []:
                for st in s.get('states', []) or []:
                    c = st.get('class')
                    if isinstance(c, str):
                        classes.add(c)
        except Exception:
            pass
        # Include keys already in maps
        try:
            classes.update(self.model.default_map.keys())
            for mm in (self.model.overrides_map or {}).values():
                classes.update(mm.keys())
        except Exception:
            pass
        return sorted(classes)

    def _build_rows(self) -> None:
        rows: List[AnimRow] = []
        target = self.model.target_set_id or 'default'
        all_classes = self._collect_all_state_classes()
        if target == 'default':
            for cls in all_classes:
                rows.append(AnimRow(state_class=cls, value=self.model.default_map.get(cls), inherited=False))
        else:
            ov = self.model.overrides_map.get(target) or {}
            for cls in all_classes:
                if cls in ov:
                    rows.append(AnimRow(state_class=cls, value=ov.get(cls), inherited=False))
                else:
                    # inherit from default if present; show None but mark inherited for UI
                    rows.append(AnimRow(state_class=cls, value=None, inherited=(cls in self.model.default_map)))
        self.model.rows = rows

    # --- Public API ---
    def render(self, screen, *, anchor=None):
        if not getattr(self.model, 'visible', False):
            return None
        # Ensure fresh data and rows
        self._load_if_needed()
        self._refresh_targets()
        self._build_rows()
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        return self.events.handle_event(self, event)

    # --- Helpers for events ---
    def _navigate_target(self, step: int) -> None:
        targets = self.model.available_targets or ['default']
        if not targets:
            return
        try:
            idx = targets.index(self.model.target_set_id)
        except ValueError:
            idx = 0
        idx = (idx + int(step)) % len(targets)
        self.model.target_set_id = targets[idx]
        # reset selection/editing on navigation
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
        target = self.model.target_set_id or 'default'
        # Apply change
        if target == 'default':
            if txt:
                self.model.default_map[row.state_class] = txt
            else:
                self.model.default_map.pop(row.state_class, None)
        else:
            mm = self.model.overrides_map.setdefault(target, {})
            if txt:
                mm[row.state_class] = txt
            else:
                mm.pop(row.state_class, None)
        # Save to disk
        try:
            from roguelike_editors.fsm.services.fsm_persistence import (
                default_animation_map_path,
                save_animation_map,
            )
            data = {
                "default": dict(sorted(self.model.default_map.items())),
                "overrides": {k: dict(sorted(v.items())) for k, v in sorted(self.model.overrides_map.items())},
            }
            save_animation_map(data, default_animation_map_path())
        except Exception:
            pass
        # Reset editing and rebuild rows to reflect inheritance changes
        self.model.editing_index = None
        self.model.editing_text = ""
        self._build_rows()


__all__ = ["FsmAssigmentAnimationsController"]

