from __future__ import annotations
import logging
from typing import Optional, Tuple

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.clone.view")


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


class CloneView:
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
        # World pick
        wx, wy = _to_world(model, lx, ly)
        src = _pick_node(model, wx, wy)
        if src is None:
            return
        # Read offsets (prefer self attributes set by controller, fall back to model defaults)
        dx = int(getattr(self, 'offset_dx', getattr(model, 'offset_dx', 20)))
        dy = int(getattr(self, 'offset_dy', getattr(model, 'offset_dy', 20)))
        # Clone rect in world coords
        nx = float(src.get('x', 0)); ny = float(src.get('y', 0))
        nw = float(src.get('w', 120)); nh = float(src.get('h', 60))
        cx, cy = nx + dx, ny + dy
        clx, cly = _to_local(model, cx, cy)
        zoom = max(0.05, float(getattr(model, 'zoom', 1.0)))
        rect = pygame.Rect(
            int(canvas_rect.left) + int(clx),
            int(canvas_rect.top) + int(cly),
            int(nw * zoom),
            int(nh * zoom),
        )
        color = tuple(getattr(self, 'preview_color', (160, 210, 255)))
        width = int(getattr(self, 'node_outline_width', 2))
        try:
            pygame.draw.rect(screen, color, rect, width)
        except Exception:
            pass


__all__ = ["CloneView"]
