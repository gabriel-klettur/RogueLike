from __future__ import annotations
from typing import Optional

from .fsm_graph_panel_model import FsmGraphPanelModel
from .fsm_graph_panel_view import FsmGraphPanelView
from .toolbar_graph_panel.toolbar_graph_panel_controller import FsmGraphToolbarController
from .toolbar_graph_panel.toolbar_graph_panel_events import FsmGraphToolbarEventHandler
from .toolbar_graph_panel.services.tools_registry import get_tool_bundle
from roguelike_editors.fsm.services.fsm_persistence import (
    default_layouts_path,
    load_layouts,
    save_layouts,
    default_sets_path,
    load_sets,
    save_sets,
)
from roguelike_editors.fsm.services.fsm_runtime_bridge import publish_reload
from .fsm_graph_panel_events import FsmGraphPanelEventHandler


class FsmGraphPanelController:
    def __init__(self, model: Optional[FsmGraphPanelModel] = None, view: Optional[FsmGraphPanelView] = None) -> None:
        self.model = model or FsmGraphPanelModel()
        self.view = view or FsmGraphPanelView()
        # Dedicated toolbar MVC for graph tools
        self.toolbar = FsmGraphToolbarController()
        self.toolbar_events = FsmGraphToolbarEventHandler()
        # Centralized events handler for the graph panel
        self.events = FsmGraphPanelEventHandler()
        # Active tool runtime (events instance) for feature-first tools
        self._active_tool_key = None
        self._active_tool_events = None
        self._active_tool_view = None
        self._active_tool_model = None
        self._active_tool_controller = None
        try:
            self._activate_tool(getattr(self.model, 'active_graph_tool', 'select'))
        except Exception:
            pass

    def render(self, screen, *, anchor=None):
        # Base render
        if anchor is None:
            result = self.view.render(self.model, screen, toolbar=self.toolbar)
        else:
            result = self.view.render(self.model, screen, anchor=anchor, toolbar=self.toolbar)
        # Optional tool overlay
        try:
            if hasattr(self.view, 'render_active_tool_overlay'):
                self.view.render_active_tool_overlay(self.model, screen, self._active_tool_view)
        except Exception:
            pass
        return result

    def _activate_tool(self, key: str) -> None:
        """Activate a graph tool by key, instantiating its Events handler if available."""
        try:
            k = str(key or 'select')
        except Exception:
            k = 'select'
        if k == getattr(self, '_active_tool_key', None):
            return
        # Deselect previous tool
        try:
            if self._active_tool_events and hasattr(self._active_tool_events, 'on_deselect'):
                self._active_tool_events.on_deselect(self, self.model, self.view)
        except Exception:
            pass
        try:
            if self._active_tool_controller and hasattr(self._active_tool_controller, 'deactivate'):
                self._active_tool_controller.deactivate()
        except Exception:
            pass
        self._active_tool_events = None
        self._active_tool_model = None
        self._active_tool_controller = None
        self._active_tool_key = k
        # No runtime handler needed for select/zoom buttons
        if k in ('select', 'zoom_in', 'zoom_out'):
            self._active_tool_view = None
            return
        try:
            bundle = get_tool_bundle(k)
            # Model
            m_cls = getattr(bundle, 'model', None)
            if m_cls:
                try:
                    self._active_tool_model = m_cls() if callable(m_cls) else m_cls
                except Exception:
                    self._active_tool_model = None
            else:
                self._active_tool_model = None
            # View
            v_cls = getattr(bundle, 'view', None)
            if v_cls:
                self._active_tool_view = v_cls() if callable(v_cls) else v_cls
            else:
                self._active_tool_view = None
            # Controller
            c_cls = getattr(bundle, 'controller', None)
            if c_cls:
                try:
                    self._active_tool_controller = c_cls() if callable(c_cls) else c_cls
                except Exception:
                    self._active_tool_controller = None
            else:
                self._active_tool_controller = None
            # Events
            ev_cls = getattr(bundle, 'events', None)
            if ev_cls:
                self._active_tool_events = ev_cls() if callable(ev_cls) else ev_cls
                if hasattr(self._active_tool_events, 'on_select'):
                    self._active_tool_events.on_select(self, self.model, self.view)
            # Attach tool controller after all parts are ready
            try:
                if self._active_tool_controller and hasattr(self._active_tool_controller, 'activate'):
                    self._active_tool_controller.activate(panel_controller=self,
                                                          tool_model=self._active_tool_model,
                                                          tool_view=self._active_tool_view)
            except Exception:
                pass
        except Exception:
            self._active_tool_events = None
            self._active_tool_view = None
            self._active_tool_model = None
            self._active_tool_controller = None

    def _dispatch_active_tool_event(self, event) -> bool:
        """Dispatch an event to the active tool events handler, if any."""
        ev = getattr(self, '_active_tool_events', None)
        if not ev:
            return False
        try:
            canvas = getattr(self.view, 'canvas_rect', None)
            return bool(ev.handle_event(self, event, model=self.model, view=self.view, canvas_rect=canvas))
        except Exception:
            return False

    def handle_event(self, event) -> bool:
        # Interactive graph canvas events are fully delegated to the centralized handler.
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        if not getattr(self.model, 'visible', False):
            return False
        if getattr(self.view, 'canvas_rect', None) is None:
            return False
        try:
            if getattr(self, 'events', None) and self.events.handle_event(self, event):
                return True
        except Exception:
            pass
        return False

    def _persist_layout(self) -> None:
        """Save current node positions for the selected set to layouts.json."""
        set_id = getattr(self.model, 'selected_set_id', None)
        if not set_id:
            return
        nodes = getattr(self.model, 'nodes', [])
        path = default_layouts_path()
        try:
            layouts = load_layouts(path)
        except FileNotFoundError:
            layouts = {"by_set": {}}
        if not isinstance(layouts, dict):
            layouts = {"by_set": {}}
        by_set = layouts.get("by_set")
        if not isinstance(by_set, dict):
            by_set = {}
        entry = by_set.get(set_id) or {}
        # Build nodes map
        nodes_map = {}
        for n in nodes:
            nid = n.get('id')
            if not nid:
                continue
            try:
                x = int(n.get('x', 0))
                y = int(n.get('y', 0))
            except Exception:
                continue
            nodes_map[nid] = {"x": x, "y": y}
        # Persist viewport (zoom, pan)
        try:
            zoom = float(getattr(self.model, 'zoom', 1.0))
        except Exception:
            zoom = 1.0
        try:
            pan_x = float(getattr(self.model, 'pan_x', 0.0))
            pan_y = float(getattr(self.model, 'pan_y', 0.0))
        except Exception:
            pan_x, pan_y = 0.0, 0.0
        entry["nodes"] = nodes_map
        entry["viewport"] = {
            "zoom": zoom,
            "pan_x": pan_x,
            "pan_y": pan_y,
            "legend_collapsed": bool(getattr(self.model, 'legend_collapsed', False)),
        }
        by_set[set_id] = entry
        layouts["by_set"] = by_set
        save_layouts(layouts, path)

    def _persist_sets_structural(self) -> None:
        """Write current nodes/edges into the selected set within sets.json.
        - Adds/removes states based on nodes
        - Updates set.initial from node flags
        - Rebuilds transitions from edges, preserving existing 'when' if possible
        """
        set_id = getattr(self.model, 'selected_set_id', None)
        if not set_id:
            return
        path = default_sets_path()
        data = load_sets(path)
        sets = (data or {}).get('sets') or []
        target = None
        for s in sets:
            if s.get('id') == set_id:
                target = s
                break
        if target is None:
            return
        # States
        existing_states = {st.get('id'): st for st in (target.get('states') or []) if isinstance(st, dict)}
        new_states = []
        initial_node_id = None
        for n in getattr(self.model, 'nodes', []):
            nid = n.get('id')
            if not nid:
                continue
            st = dict(existing_states.get(nid) or {'id': nid})
            # Keep label if present, else from node
            if 'label' not in st or not st.get('label'):
                if n.get('label'):
                    st['label'] = n.get('label')
            # Flags
            if n.get('initial'):
                initial_node_id = nid
            st['terminal'] = bool(n.get('terminal', st.get('terminal', False)))
            new_states.append(st)
        target['states'] = new_states
        # Initial selection: prefer explicitly marked node; otherwise keep previous if still present; else fallback to first
        new_ids = [st.get('id') for st in new_states if isinstance(st.get('id'), str)]
        if initial_node_id:
            target['initial'] = initial_node_id
        else:
            prev_init = target.get('initial')
            if prev_init not in new_ids:
                if new_ids:
                    target['initial'] = new_ids[0]
        # Transitions: build from edges, preserve existing 'when' if possible
        existing_trs = target.get('transitions') or []
        by_pair = {}
        for tr in existing_trs:
            key = (tr.get('from'), tr.get('to'))
            by_pair.setdefault(key, []).append(tr)
        new_trs = []
        for e in getattr(self.model, 'edges', []):
            fr = e.get('from'); to = e.get('to')
            if not fr or not to:
                continue
            key = (fr, to)
            carry = (by_pair.get(key) or [None])[0]
            when = e.get('label') if isinstance(e.get('label'), str) else (carry.get('when') if isinstance(carry, dict) else '')
            tr = {'from': fr, 'to': to, 'when': when}
            # Preserve optional style/fields
            if isinstance(carry, dict):
                for k in ('conditions', 'actions', 'style', 'color', 'width', 'head_len', 'head_width', 'curved', 'curve_step', 'active'):
                    if k in carry and k not in tr:
                        tr[k] = carry[k]
            for k in ('color', 'width', 'head_len', 'head_width', 'curved', 'curve_step', 'active'):
                if k in e:
                    tr[k] = e[k]
            new_trs.append(tr)
        target['transitions'] = new_trs
        # Save (validates and codegens via save_sets)
        save_sets(data, path)
        # Hot-reload runtime snapshot
        try:
            publish_reload()
        except Exception:
            pass


__all__ = ["FsmGraphPanelController"]
