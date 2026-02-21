from __future__ import annotations
import logging
from typing import Optional

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.mark_end.controller")


class MarkEndController:
    """Optional controller for the MarkEnd tool.
    Applies visual configuration from the tool model (or ctor kwargs) to the view on activation.
    """

    def __init__(
        self,
        *,
        node_highlight_color=None,
        node_outline_width: Optional[int] = None,
    ) -> None:
        self.node_highlight_color = node_highlight_color
        self.node_outline_width = node_outline_width
        self._panel = None
        self._tool_model = None
        self._tool_view = None

    def activate(self, *, panel_controller, tool_model=None, tool_view=None) -> None:
        self._panel = panel_controller
        self._tool_model = tool_model
        self._tool_view = tool_view
        # Apply configuration to the view. Priority: ctor overrides > model defaults.
        if self._tool_view is not None:
            try:
                nhc = self.node_highlight_color
                now = self.node_outline_width
                if tool_model is not None:
                    nhc = nhc if nhc is not None else getattr(tool_model, 'node_highlight_color', None)
                    now = now if now is not None else getattr(tool_model, 'node_outline_width', None)
                if nhc is not None:
                    setattr(self._tool_view, 'node_highlight_color', tuple(nhc))
                if now is not None:
                    setattr(self._tool_view, 'node_outline_width', int(now))
            except Exception:
                LOGGER.exception("[MarkEndController] failed to apply config to view")
        LOGGER.debug("[MarkEndController] activated")

    def deactivate(self) -> None:
        LOGGER.debug("[MarkEndController] deactivated")
        self._panel = None
        self._tool_model = None
        self._tool_view = None


__all__ = ["MarkEndController"]
