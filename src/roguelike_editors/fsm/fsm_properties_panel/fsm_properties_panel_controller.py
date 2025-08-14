from __future__ import annotations
from typing import Optional, Dict, Any, List, Tuple

from .fsm_properties_panel_models import FsmPropertiesPanelModel, Row
from .fsm_properties_panel_view import FsmPropertiesPanelView
from .fsm_properties_panel_events import FsmPropertiesPanelEventHandler


class FsmPropertiesPanelController:
    def __init__(
        self,
        model: Optional[FsmPropertiesPanelModel] = None,
        view: Optional[FsmPropertiesPanelView] = None,
        events: Optional[FsmPropertiesPanelEventHandler] = None,
    ) -> None:
        self.model = model or FsmPropertiesPanelModel()
        self.view = view or FsmPropertiesPanelView()
        self.events = events or FsmPropertiesPanelEventHandler()

        # Cached snapshot for current frame
        self._snapshot: Dict[str, Any] = {}

    # --- Rendering ---
    def render(self, screen, *, anchor=None):
        if not getattr(self.model, 'visible', False):
            return None
        # Refresh data and rows before rendering
        self._refresh_snapshot()
        self._refresh_sets()
        self._refresh_items()
        self._build_rows()
        if anchor is None:
            return self.view.render(self.model, screen)
        return self.view.render(self.model, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        return self.events.handle_event(self, event)

    # --- Data loading ---
    def _refresh_snapshot(self) -> None:
        try:
            from roguelike_editors.fsm.services.fsm_runtime_bridge import get_snapshot
            self._snapshot = get_snapshot() or {}
        except Exception:
            self._snapshot = {}

    def _sets_list(self) -> List[Dict[str, Any]]:
        try:
            return list(self._snapshot.get('sets', []) or [])
        except Exception:
            return []

    def _refresh_sets(self) -> None:
        ids = []
        for s in self._sets_list():
            sid = s.get('id')
            if isinstance(sid, str):
                ids.append(sid)
        self.model.set_ids = ids
        if not self.model.selected_set_id and ids:
            self.model.selected_set_id = ids[0]
        # Clamp selection if set was removed
        if self.model.selected_set_id and self.model.selected_set_id not in ids:
            self.model.selected_set_id = ids[0] if ids else None

    def _current_set(self) -> Optional[Dict[str, Any]]:
        sid = self.model.selected_set_id
        if not sid:
            return None
        for s in self._sets_list():
            if s.get('id') == sid:
                return s
        return None

    def _refresh_items(self) -> None:
        s = self._current_set()
        if not s:
            self.model.node_ids = []
            self.model.transition_labels = []
            self.model.selected_node_id = None
            self.model.selected_transition_index = None
            return
        if self.model.active_tab == 'nodes':
            node_ids = []
            for st in s.get('states', []) or []:
                nid = st.get('id')
                if isinstance(nid, str):
                    node_ids.append(nid)
            self.model.node_ids = node_ids
            if (not self.model.selected_node_id) and node_ids:
                self.model.selected_node_id = node_ids[0]
            if self.model.selected_node_id and self.model.selected_node_id not in node_ids:
                self.model.selected_node_id = node_ids[0] if node_ids else None
        else:
            labels = []
            trs = s.get('transitions', []) or []
            for tr in trs:
                fr = tr.get('from'); to = tr.get('to')
                labels.append(f"{fr} -> {to}")
            self.model.transition_labels = labels
            if (self.model.selected_transition_index is None) and labels:
                self.model.selected_transition_index = 0
            if self.model.selected_transition_index is not None:
                if not (0 <= int(self.model.selected_transition_index) < len(labels)):
                    self.model.selected_transition_index = 0 if labels else None

    # --- Rows build ---
    def _build_rows(self) -> None:
        rows: List[Row] = []
        s = self._current_set()
        if not s:
            self.model.rows = rows
            return
        if self.model.active_tab == 'nodes':
            node = None
            nid = self.model.selected_node_id
            for st in s.get('states', []) or []:
                if st.get('id') == nid:
                    node = st
                    break
            if node:
                rows.append(Row(key='id', value=str(node.get('id')), editable=False))
                rows.append(Row(key='class', value=str(node.get('class') or ''), editable=True))
                props = node.get('props') or {}
                if isinstance(props, dict):
                    for k, v in props.items():
                        rows.append(Row(key=f"props.{k}", value=str(v) if v is not None else '', editable=True))
        else:
            idx = self.model.selected_transition_index
            trs = s.get('transitions', []) or []
            tr = trs[int(idx)] if (idx is not None and 0 <= int(idx) < len(trs)) else None
            if tr:
                rows.append(Row(key='from', value=str(tr.get('from')), editable=False))
                rows.append(Row(key='to', value=str(tr.get('to')), editable=False))
                rows.append(Row(key='when', value=str(tr.get('when') or ''), editable=True))
                # Optional style keys if present
                for k in ('color', 'width', 'head_len', 'head_width', 'curved', 'curve_step', 'active'):
                    if k in tr:
                        rows.append(Row(key=k, value=str(tr.get(k)), editable=True))
        self.model.rows = rows

    # --- Navigation helpers (called by events) ---
    def _switch_tab(self, tab: str) -> None:
        tab = 'nodes' if tab != 'transitions' else 'transitions'
        if self.model.active_tab != tab:
            self.model.active_tab = tab
            self.model.selected_index = None
            self.model.editing_index = None
            self.model.editing_text = ''
            self._refresh_items()
            self._build_rows()

    def _navigate_set(self, step: int) -> None:
        ids = self.model.set_ids or []
        if not ids:
            return
        try:
            idx = ids.index(self.model.selected_set_id)
        except ValueError:
            idx = 0
        idx = (idx + int(step)) % len(ids)
        self.model.selected_set_id = ids[idx]
        # Reset selection
        self.model.selected_node_id = None
        self.model.selected_transition_index = None
        self.model.selected_index = None
        self.model.editing_index = None
        self.model.editing_text = ''
        self._refresh_items()
        self._build_rows()

    def _navigate_item(self, step: int) -> None:
        if self.model.active_tab == 'nodes':
            items = self.model.node_ids or []
            if not items:
                return
            cur = self.model.selected_node_id
            try:
                idx = items.index(cur)
            except ValueError:
                idx = 0
            idx = (idx + int(step)) % len(items)
            self.model.selected_node_id = items[idx]
        else:
            items = self.model.transition_labels or []
            if not items:
                return
            idx = self.model.selected_transition_index or 0
            idx = (idx + int(step)) % len(items)
            self.model.selected_transition_index = idx
        # Reset editing and rebuild
        self.model.selected_index = None
        self.model.editing_index = None
        self.model.editing_text = ''
        self._build_rows()

    # --- Commit editing ---
    def _commit_edit(self) -> None:
        idx = self.model.editing_index
        if idx is None or not (0 <= int(idx) < len(self.model.rows)):
            self.model.editing_index = None
            self.model.editing_text = ''
            return
        row = self.model.rows[int(idx)]
        if not row.editable:
            self.model.editing_index = None
            self.model.editing_text = ''
            return
        key = row.key
        new_val_raw = (self.model.editing_text or '').strip()

        # Load sets doc from disk, apply change, save
        try:
            from roguelike_editors.fsm.services.fsm_persistence import (
                default_sets_path,
                load_sets,
                save_sets,
                default_schema_path,
                validate,
            )
            from roguelike_editors.fsm.services.fsm_runtime_bridge import publish_reload
            path = default_sets_path()
            data = load_sets(path)
            # locate set
            target_sid = self.model.selected_set_id
            target_set = None
            for s in data.get('sets', []) or []:
                if s.get('id') == target_sid:
                    target_set = s
                    break
            if not target_set:
                raise RuntimeError('Selected set not found during commit')

            if self.model.active_tab == 'nodes':
                # locate node
                nid = self.model.selected_node_id
                node = None
                for st in target_set.get('states', []) or []:
                    if st.get('id') == nid:
                        node = st
                        break
                if not node:
                    raise RuntimeError('Selected node not found during commit')
                if key == 'class':
                    node['class'] = new_val_raw
                elif key.startswith('props.'):
                    prop_key = key.split('.', 1)[1]
                    props = node.setdefault('props', {})
                    props[prop_key] = new_val_raw
            else:
                # transitions
                t_idx = self.model.selected_transition_index or 0
                trs = target_set.get('transitions', []) or []
                if not (0 <= int(t_idx) < len(trs)):
                    raise RuntimeError('Selected transition not found during commit')
                tr = trs[int(t_idx)]
                if key == 'when':
                    tr['when'] = new_val_raw
                else:
                    # Allow editing present style keys
                    tr[key] = new_val_raw

            # Validate if possible (no-op if missing)
            try:
                validate(data, default_schema_path())
            except Exception:
                # Keep saving even if schema warns
                pass
            save_sets(data, path)
            publish_reload()
        except Exception:
            # swallow errors in editor
            pass
        finally:
            # Reset editing and refresh view rows from snapshot
            self.model.editing_index = None
            self.model.editing_text = ''
            self._refresh_snapshot()
            self._refresh_items()
            self._build_rows()


__all__ = ["FsmPropertiesPanelController"]

