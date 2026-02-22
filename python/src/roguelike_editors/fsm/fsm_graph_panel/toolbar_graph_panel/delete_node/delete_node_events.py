from __future__ import annotations
import logging
from typing import Optional, List, Tuple
from ...services import persist_layout, persist_sets_structural

LOGGER = logging.getLogger("roguelike_editors.fsm.fsm_graph_panel.tools.delete")


def _to_world(model, lx: int, ly: int):
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
    # distance from point P to segment AB
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


def _pick_edge_index_local(view, lx: int, ly: int) -> Optional[int]:
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
    # tolerance in local space (pixels)
    tol = int(getattr(view, 'edge_pick_tolerance', 8))
    if best_d <= tol:
        try:
            return int(best_idx)  # some keys are indices
        except Exception:
            return None
    return None


class DeleteNodeEventHandler:
    def on_select(self, controller, model, view) -> None:
        LOGGER.debug("[Delete] selected")

    def on_deselect(self, controller, model, view) -> None:
        LOGGER.debug("[Delete] deselected")

    def handle_event(self, controller, event, *, model, view, canvas_rect) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        if canvas_rect is None:
            return False
        if getattr(event, 'type', None) != pygame.MOUSEBUTTONDOWN or getattr(event, 'button', None) != 1:
            return False
        mouse = getattr(event, 'pos', None) or pygame.mouse.get_pos()
        if not canvas_rect.collidepoint(mouse):
            return False
        # Local and world
        lx = mouse[0] - canvas_rect.left
        ly = mouse[1] - canvas_rect.top
        wx, wy = _to_world(model, lx, ly)
        # Try node first
        node = _pick_node(model, wx, wy)
        if node is not None:
            nid = node.get('id')
            if nid:
                try:
                    model.nodes = [n for n in (model.nodes or []) if n.get('id') != nid]
                except Exception:
                    pass
                try:
                    model.edges = [e for e in (model.edges or []) if e.get('from') != nid and e.get('to') != nid]
                except Exception:
                    pass
                try:
                    if getattr(model, 'selected_node_id', None) == nid:
                        model.selected_node_id = None
                    if getattr(model, 'hover_node_id', None) == nid:
                        model.hover_node_id = None
                except Exception:
                    pass
                try:
                    model.rebuild_caches()
                except Exception:
                    pass
                try:
                    persist_sets_structural(model)
                except Exception:
                    pass
                try:
                    persist_layout(model)
                except Exception:
                    pass
                LOGGER.debug("[Delete] removed node %s and its edges", nid)
                return True
        # Else try edge (by hover id or proximity)
        edge_id = getattr(model, 'hover_edge_id', None)
        if not edge_id:
            idx = _pick_edge_index_local(view, lx, ly)
            if isinstance(idx, int):
                try:
                    if len(getattr(model, 'edge_id_by_index', []) or []) != len(getattr(model, 'edges', []) or []):
                        model.rebuild_caches()
                    if 0 <= idx < len(model.edge_id_by_index):
                        edge_id = model.edge_id_by_index[idx]
                except Exception:
                    edge_id = None
        if edge_id:
            # remove by id
            try:
                ei = (model.edge_index_by_id or {}).get(edge_id)
                if isinstance(ei, int):
                    del model.edges[ei]
                else:
                    model.edges = [e for e in (model.edges or []) if e.get('id') != edge_id]
            except Exception:
                pass
            try:
                if getattr(model, 'selected_edge_id', None) == edge_id:
                    model.selected_edge_id = None
                if getattr(model, 'hover_edge_id', None) == edge_id:
                    model.hover_edge_id = None
                model.selected_edge_index = None
                model.hover_edge_index = None
            except Exception:
                pass
            try:
                model.rebuild_caches()
            except Exception:
                pass
            try:
                persist_sets_structural(model)
            except Exception:
                pass
            try:
                persist_layout(model)
            except Exception:
                pass
            LOGGER.debug("[Delete] removed edge id=%s", edge_id)
            return True
        # Nothing targeted; still consume to block selection tool
        return True


__all__ = ["DeleteNodeEventHandler"]
