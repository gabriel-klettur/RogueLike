from __future__ import annotations
import logging
from typing import Tuple

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.mark_end.view")


def _to_local(model, wx: float, wy: float) -> Tuple[int, int]:
    try:
        zoom = max(0.05, float(getattr(model, 'zoom', 1.0)))
    except Exception:
        zoom = 1.0
    pan_x = float(getattr(model, 'pan_x', 0.0))
    pan_y = float(getattr(model, 'pan_y', 0.0))
    return (int(wx * zoom + pan_x), int(wy * zoom + pan_y))


def _to_world(model, lx: int, ly: int) -> Tuple[float, float]:
    z = max(0.05, float(getattr(model, 'zoom', 1.0)))
    return ((lx - float(getattr(model, 'pan_x', 0.0))) / z, (ly - float(getattr(model, 'pan_y', 0.0))) / z)


def _pick_node(model, wx: float, wy: float):
    nodes = getattr(model, 'nodes', []) or []
    for n in reversed(list(nodes)):
        nx = int(n.get('x', 0)); ny = int(n.get('y', 0))
        nw = int(n.get('w', 120)); nh = int(n.get('h', 60))
        if nx <= wx <= nx + nw and ny <= wy <= ny + nh:
            return n
    return None


class MarkEndView:
    def render_overlay(self, *, model, screen, canvas_rect, view) -> None:
        try:
            import pygame  # type: ignore
        except Exception:
            return
        if canvas_rect is None:
            return
        try:
            mx, my = pygame.mouse.get_pos()
        except Exception:
            return
        if not canvas_rect.collidepoint((mx, my)):
            return
        # Local mouse
        lx = int(mx - int(canvas_rect.left))
        ly = int(my - int(canvas_rect.top))
        # World pick for nodes
        wx, wy = _to_world(model, lx, ly)
        node = _pick_node(model, wx, wy)
        if node is None:
            return
        # Draw node highlight in local space
        nx = float(node.get('x', 0)); ny = float(node.get('y', 0))
        nw = float(node.get('w', 120)); nh = float(node.get('h', 60))
        nlx, nly = _to_local(model, nx, ny)
        try:
            zoom = max(0.05, float(getattr(model, 'zoom', 1.0)))
        except Exception:
            zoom = 1.0
        rect = pygame.Rect(
            int(canvas_rect.left) + int(nlx),
            int(canvas_rect.top) + int(nly),
            int(nw * zoom),
            int(nh * zoom),
        )
        color = tuple(getattr(self, 'node_highlight_color', (200, 200, 255)))
        width = int(getattr(self, 'node_outline_width', 3))
        try:
            pygame.draw.rect(screen, color, rect, width)
        except Exception:
            pass


__all__ = ["MarkEndView"]
