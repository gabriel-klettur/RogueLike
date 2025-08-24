from __future__ import annotations
import logging
from typing import Optional

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.delete.controller")


class DeleteNodeController:
    """Optional controller for the Delete tool.
    Applies visual configuration from the tool model (or ctor kwargs) to the view on activation.
    """

    def __init__(
        self,
        *,
        node_highlight_color=None,
        node_outline_width: Optional[int] = None,
        edge_highlight_color=None,
        edge_highlight_width: Optional[int] = None,
        edge_pick_tolerance: Optional[int] = None,
    ) -> None:
        self.node_highlight_color = node_highlight_color
        self.node_outline_width = node_outline_width
        self.edge_highlight_color = edge_highlight_color
        self.edge_highlight_width = edge_highlight_width
        self.edge_pick_tolerance = edge_pick_tolerance
        self._panel = None
        self._tool_model = None
        self._tool_view = None
        self._saved_panel_edge_pick_tolerance = None
        self._panel_had_edge_pick_tolerance = False

    def activate(self, *, panel_controller, tool_model=None, tool_view=None) -> None:
        self._panel = panel_controller
        self._tool_model = tool_model
        self._tool_view = tool_view
        if self._tool_view is not None:
            try:
                # Priority: ctor overrides > model defaults
                nhc = self.node_highlight_color
                now = self.node_outline_width
                ehc = self.edge_highlight_color
                ehw = self.edge_highlight_width
                ept = self.edge_pick_tolerance
                if tool_model is not None:
                    nhc = nhc if nhc is not None else getattr(tool_model, 'node_highlight_color', None)
                    now = now if now is not None else getattr(tool_model, 'node_outline_width', None)
                    ehc = ehc if ehc is not None else getattr(tool_model, 'edge_highlight_color', None)
                    ehw = ehw if ehw is not None else getattr(tool_model, 'edge_highlight_width', None)
                    ept = ept if ept is not None else getattr(tool_model, 'edge_pick_tolerance', None)
                if nhc is not None:
                    setattr(self._tool_view, 'node_highlight_color', tuple(nhc))
                if now is not None:
                    setattr(self._tool_view, 'node_outline_width', int(now))
                if ehc is not None:
                    setattr(self._tool_view, 'edge_highlight_color', tuple(ehc))
                if ehw is not None:
                    setattr(self._tool_view, 'edge_highlight_width', int(ehw))
                if ept is not None:
                    setattr(self._tool_view, 'edge_pick_tolerance', int(ept))
            except Exception:
                LOGGER.exception("[DeleteNodeController] failed to apply config to view")
        # Also expose tolerance to the main panel view so the events handler can read it
        try:
            panel_view = getattr(self._panel, 'view', None)
            if panel_view is not None:
                self._panel_had_edge_pick_tolerance = hasattr(panel_view, 'edge_pick_tolerance')
                if self._panel_had_edge_pick_tolerance:
                    self._saved_panel_edge_pick_tolerance = getattr(panel_view, 'edge_pick_tolerance')
                # Determine ept value again with same priority
                ept = self.edge_pick_tolerance
                if ept is None and tool_model is not None:
                    ept = getattr(tool_model, 'edge_pick_tolerance', None)
                if ept is not None:
                    setattr(panel_view, 'edge_pick_tolerance', int(ept))
        except Exception:
            pass
        LOGGER.debug("[DeleteNodeController] activated")

    def deactivate(self) -> None:
        LOGGER.debug("[DeleteNodeController] deactivated")
        # Restore panel view attribute if we changed it
        try:
            panel_view = getattr(self._panel, 'view', None)
            if panel_view is not None:
                if self._panel_had_edge_pick_tolerance:
                    setattr(panel_view, 'edge_pick_tolerance', self._saved_panel_edge_pick_tolerance)
                else:
                    try:
                        delattr(panel_view, 'edge_pick_tolerance')
                    except Exception:
                        pass
        except Exception:
            pass
        self._panel = None
        self._tool_model = None
        self._tool_view = None


__all__ = ["DeleteNodeController"]
