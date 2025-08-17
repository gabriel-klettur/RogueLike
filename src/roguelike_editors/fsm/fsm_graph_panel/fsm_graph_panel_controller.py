from __future__ import annotations
from typing import Optional

from .fsm_graph_panel_model import FsmGraphPanelModel
from .fsm_graph_panel_view import FsmGraphPanelView
from .toolbar_graph_panel.toolbar_graph_panel_controller import FsmGraphToolbarController
from .toolbar_graph_panel.toolbar_graph_panel_events import FsmGraphToolbarEventHandler
from .toolbar_graph_panel.services.tools_registry import get_tool_bundle
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


__all__ = ["FsmGraphPanelController"]
