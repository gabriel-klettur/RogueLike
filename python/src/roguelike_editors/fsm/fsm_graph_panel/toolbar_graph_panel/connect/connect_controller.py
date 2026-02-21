from __future__ import annotations
import logging
from typing import Optional

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.connect.controller")


class ConnectController:
    """Optional controller for the Connect tool.
    Wires tool model configuration into the tool view on activation and offers
    a place to add future interaction state beyond raw events.
    """

    def __init__(self, *, preview_color=None, arrow_head_len: Optional[int] = None, arrow_head_width: Optional[int] = None) -> None:
        self.preview_color = preview_color
        self.arrow_head_len = arrow_head_len
        self.arrow_head_width = arrow_head_width
        self._panel = None
        self._tool_model = None
        self._tool_view = None

    def activate(self, *, panel_controller, tool_model=None, tool_view=None) -> None:
        self._panel = panel_controller
        self._tool_model = tool_model
        self._tool_view = tool_view
        # Apply configuration to the view from (1) kwargs override, (2) tool model defaults
        if self._tool_view is not None:
            try:
                color = self.preview_color
                ahl = self.arrow_head_len
                ahw = self.arrow_head_width
                if color is None and tool_model is not None:
                    color = getattr(tool_model, 'preview_color', None)
                if ahl is None and tool_model is not None:
                    ahl = getattr(tool_model, 'arrow_head_len', None)
                if ahw is None and tool_model is not None:
                    ahw = getattr(tool_model, 'arrow_head_width', None)
                if color is not None:
                    setattr(self._tool_view, 'preview_color', tuple(color))
                if ahl is not None:
                    setattr(self._tool_view, 'arrow_head_len', int(ahl))
                if ahw is not None:
                    setattr(self._tool_view, 'arrow_head_width', int(ahw))
            except Exception:
                LOGGER.exception("[ConnectController] failed to apply config to view")
        LOGGER.debug("[ConnectController] activated")

    def deactivate(self) -> None:
        LOGGER.debug("[ConnectController] deactivated")
        self._panel = None
        self._tool_model = None
        self._tool_view = None


__all__ = ["ConnectController"]
