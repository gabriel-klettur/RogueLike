from __future__ import annotations
from typing import Optional, Tuple

from .toolbar_graph_panel_model import FsmGraphToolbarModel
from .toolbar_graph_panel_view import FsmGraphToolbarView


class FsmGraphToolbarController:
    def __init__(self, model: Optional[FsmGraphToolbarModel] = None, view: Optional[FsmGraphToolbarView] = None) -> None:
        self.model = model or FsmGraphToolbarModel()
        self.view = view or FsmGraphToolbarView()

    def render_into(self, surface, *, screen_origin: Tuple[int, int], width: int, active_tool: Optional[str] = None) -> int:
        return int(self.view.render_into(surface, self.model, screen_origin=screen_origin, width=width, active_tool=active_tool) or 0)

    def handle_mouse_down(self, mouse_pos, canvas_rect, graph_model) -> bool:
        """
        Handle a left-click on the toolbar if the mouse is over any button.
        Mutates graph_model.active_graph_tool and zoom/pan for zoom buttons.
        Returns True if the click was handled by the toolbar.
        """
        try:
            import pygame  # type: ignore
        except Exception:
            pygame = None
        rects = getattr(self.model, 'rects_abs', {}) or {}
        if not rects:
            return False
        # Hit-test
        for tool_key, r in rects.items():
            if r.collidepoint(mouse_pos):
                if tool_key in ('select', 'add_node', 'clone_node', 'connect', 'disconnect', 'delete', 'mark_ini', 'mark_end'):
                    graph_model.active_graph_tool = tool_key
                    return True
                if tool_key in ('zoom_in', 'zoom_out'):
                    factor = 1.1 if tool_key == 'zoom_in' else (1/1.1)
                    old_z = max(0.05, float(getattr(graph_model, 'zoom', 1.0)))
                    new_z = max(0.2, min(3.0, old_z * factor))
                    if abs(new_z - old_z) > 1e-6 and canvas_rect is not None:
                        # Zoom around canvas center
                        cx = canvas_rect.left + canvas_rect.w // 2
                        cy = canvas_rect.top + canvas_rect.h // 2
                        lcx = cx - canvas_rect.left
                        lcy = cy - canvas_rect.top
                        pan_x = float(getattr(graph_model, 'pan_x', 0.0))
                        pan_y = float(getattr(graph_model, 'pan_y', 0.0))
                        # world under center before zoom
                        wx = (lcx - pan_x) / old_z
                        wy = (lcy - pan_y) / old_z
                        graph_model.zoom = new_z
                        graph_model.pan_x = lcx - wx * new_z
                        graph_model.pan_y = lcy - wy * new_z
                    return True
                return False
        return False


__all__ = ["FsmGraphToolbarController"]
