from __future__ import annotations
import logging
from typing import Optional

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.delete.view")


def _to_local(model, wx: float, wy: float) -> tuple[int, int]:
    try:
        zoom = max(0.05, float(getattr(model, 'zoom', 1.0)))
    except Exception:
        zoom = 1.0
    pan_x = float(getattr(model, 'pan_x', 0.0))
    pan_y = float(getattr(model, 'pan_y', 0.0))
    return (int(wx * zoom + pan_x), int(wy * zoom + pan_y))


def _to_world(model, lx: int, ly: int) -> tuple[float, float]:
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


def _dist_pt_seg(px: float, py: float, ax: float, ay: float, bx: float, by: float) -> float:
    vx, vy = (bx - ax), (by - ay)
    wx, wy = (px - ax), (py - ay)
    c1 = vx * wx + vy * wy
    if c1 <= 0:
        dx, dy = wx, wy
        return (dx*dx + dy*dy) ** 0.5
    c2 = vx * vx + vy * vy
    if c2 <= c1:
        dx, dy = px - bx, py - by
        return (dx*dx + dy*dy) ** 0.5
    t = c1 / c2
    projx = ax + t * vx
    projy = ay + t * vy
    dx, dy = px - projx, py - projy
    return (dx*dx + dy*dy) ** 0.5


def _pick_edge_index_local(view, lx: int, ly: int, tol: int) -> Optional[int]:
    paths = getattr(view, 'edge_paths', {}) or {}
    best_idx = None
    best_d = 1e9
    try:
        for idx, pts in paths.items():
            if not isinstance(pts, list) or len(pts) < 2:
                continue
            for i in range(len(pts) - 1):
                ax, ay = pts[i]
                bx, by = pts[i + 1]
                d = _dist_pt_seg(lx, ly, ax, ay, bx, by)
                if d < best_d:
                    best_d = d
                    best_idx = idx
    except Exception:
        return None
    if best_d <= tol:
        try:
            return int(best_idx)
        except Exception:
            return None
    return None


class DeleteNodeView:
    def render_overlay(self, *, model, screen, canvas_rect, view):
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
        # First: node under mouse (world pick)
        wx, wy = _to_world(model, lx, ly)
        node = _pick_node(model, wx, wy)
        if node is not None:
            # Highlight node bounds
            nx = float(node.get('x', 0)); ny = float(node.get('y', 0))
            nw = float(node.get('w', 120)); nh = float(node.get('h', 60))
            nlx, nly = _to_local(model, nx, ny)
            rect = pygame.Rect(
                int(canvas_rect.left) + int(nlx),
                int(canvas_rect.top) + int(nly),
                int(nw * max(0.05, float(getattr(model, 'zoom', 1.0)))) ,
                int(nh * max(0.05, float(getattr(model, 'zoom', 1.0)))) ,
            )
            color = tuple(getattr(self, 'node_highlight_color', (255, 140, 140)))
            width = int(getattr(self, 'node_outline_width', 3))
            try:
                pygame.draw.rect(screen, color, rect, width)
            except Exception:
                pass
            return
        # Else: edge under mouse (local pick by proximity)
        tol = int(getattr(self, 'edge_pick_tolerance', 8))
        idx = _pick_edge_index_local(view, lx, ly, tol)
        if isinstance(idx, int):
            paths = getattr(view, 'edge_paths', {}) or {}
            pts = paths.get(idx)
            if isinstance(pts, list) and len(pts) >= 2:
                color = tuple(getattr(self, 'edge_highlight_color', (240, 90, 90)))
                width = int(getattr(self, 'edge_highlight_width', 3))
                # Draw in absolute coords
                offx = int(canvas_rect.left); offy = int(canvas_rect.top)
                try:
                    for i in range(len(pts) - 1):
                        ax, ay = pts[i]
                        bx, by = pts[i + 1]
                        pygame.draw.line(screen, color, (offx + int(ax), offy + int(ay)), (offx + int(bx), offy + int(by)), width)
                except Exception:
                    pass


__all__ = ["DeleteNodeView"]
