from __future__ import annotations
import logging
from typing import Optional

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.clone.controller")


class CloneController:
    """Optional controller for the Clone tool.
    Applies visual configuration from the tool model (or ctor kwargs) to the view on activation
    and exposes clone offsets to the main panel view so the event handler can read them.
    """

    def __init__(
        self,
        *,
        preview_color=None,
        node_outline_width: Optional[int] = None,
        offset_dx: Optional[int] = None,
        offset_dy: Optional[int] = None,
    ) -> None:
        self.preview_color = preview_color
        self.node_outline_width = node_outline_width
        self.offset_dx = offset_dx
        self.offset_dy = offset_dy
        self._panel = None
        self._tool_model = None
        self._tool_view = None
        # Save/restore panel view attributes
        self._saved_dx = None
        self._saved_dy = None
        self._panel_had_dx = False
        self._panel_had_dy = False

    def activate(self, *, panel_controller, tool_model=None, tool_view=None) -> None:
        self._panel = panel_controller
        self._tool_model = tool_model
        self._tool_view = tool_view
        # Apply to tool view
        if self._tool_view is not None:
            try:
                color = self.preview_color
                w = self.node_outline_width
                dx = self.offset_dx
                dy = self.offset_dy
                if tool_model is not None:
                    color = color if color is not None else getattr(tool_model, 'preview_color', None)
                    w = w if w is not None else getattr(tool_model, 'node_outline_width', None)
                    dx = dx if dx is not None else getattr(tool_model, 'offset_dx', None)
                    dy = dy if dy is not None else getattr(tool_model, 'offset_dy', None)
                if color is not None:
                    setattr(self._tool_view, 'preview_color', tuple(color))
                if w is not None:
                    setattr(self._tool_view, 'node_outline_width', int(w))
                if dx is not None:
                    setattr(self._tool_view, 'offset_dx', int(dx))
                if dy is not None:
                    setattr(self._tool_view, 'offset_dy', int(dy))
            except Exception:
                LOGGER.exception("[CloneController] failed to apply config to view")
        # Expose offsets to main panel view for the events handler
        try:
            panel_view = getattr(self._panel, 'view', None)
            if panel_view is not None:
                self._panel_had_dx = hasattr(panel_view, 'clone_offset_dx')
                self._panel_had_dy = hasattr(panel_view, 'clone_offset_dy')
                if self._panel_had_dx:
                    self._saved_dx = getattr(panel_view, 'clone_offset_dx')
                if self._panel_had_dy:
                    self._saved_dy = getattr(panel_view, 'clone_offset_dy')
                # Decide offsets with same priority
                dx = self.offset_dx
                dy = self.offset_dy
                if tool_model is not None:
                    dx = dx if dx is not None else getattr(tool_model, 'offset_dx', None)
                    dy = dy if dy is not None else getattr(tool_model, 'offset_dy', None)
                if dx is not None:
                    setattr(panel_view, 'clone_offset_dx', int(dx))
                if dy is not None:
                    setattr(panel_view, 'clone_offset_dy', int(dy))
        except Exception:
            pass
        LOGGER.debug("[CloneController] activated")

    def deactivate(self) -> None:
        LOGGER.debug("[CloneController] deactivated")
        # Restore panel view attributes
        try:
            panel_view = getattr(self._panel, 'view', None)
            if panel_view is not None:
                if self._panel_had_dx:
                    setattr(panel_view, 'clone_offset_dx', self._saved_dx)
                else:
                    try:
                        delattr(panel_view, 'clone_offset_dx')
                    except Exception:
                        pass
                if self._panel_had_dy:
                    setattr(panel_view, 'clone_offset_dy', self._saved_dy)
                else:
                    try:
                        delattr(panel_view, 'clone_offset_dy')
                    except Exception:
                        pass
        except Exception:
            pass
        self._panel = None
        self._tool_model = None
        self._tool_view = None


__all__ = ["CloneController"]
