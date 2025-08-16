from __future__ import annotations

from typing import Any, Dict, Optional, Tuple


def pick_node_world(model: Any, wx: float, wy: float) -> Optional[Dict[str, Any]]:
    """Return the node dict at world position (wx, wy), if any.
    Iterates in reverse to favor visually-topmost nodes (last drawn).
    """
    try:
        nodes = getattr(model, "nodes", []) or []
        for n in reversed(list(nodes)):
            nx = int(n.get("x", 0)); ny = int(n.get("y", 0))
            nw = int(n.get("w", 120)); nh = int(n.get("h", 60))
            if nx <= wx <= nx + nw and ny <= wy <= ny + nh:
                return n
    except Exception:
        pass
    return None


def pick_node_id_world(model: Any, wx: float, wy: float) -> Optional[str]:
    n = pick_node_world(model, wx, wy)
    return n.get("id") if isinstance(n, dict) else None


def _dist_pt_seg(px: float, py: float, ax: float, ay: float, bx: float, by: float) -> float:
    vx, vy = bx - ax, by - ay
    wx0, wy0 = px - ax, py - ay
    vv = vx * vx + vy * vy
    if vv <= 1e-6:
        dx, dy = px - ax, py - ay
        try:
            import math
            return math.hypot(dx, dy)
        except Exception:
            return (dx * dx + dy * dy) ** 0.5
    t = max(0.0, min(1.0, (wx0 * vx + wy0 * vy) / vv))
    cx, cy = ax + t * vx, ay + t * vy
    try:
        import math
        return math.hypot(px - cx, py - cy)
    except Exception:
        dx, dy = px - cx, py - cy
        return (dx * dx + dy * dy) ** 0.5


def pick_edge_local(view: Any, lx: int, ly: int, proximity_px: int = 8) -> Optional[int]:
    """Return hovered edge index using local (canvas) coordinates.
    Prefers label hit; otherwise uses polyline proximity with tolerance.
    """
    try:
        ex, ey = int(lx), int(ly)
        # Label rects first
        label_rects = getattr(view, "edge_label_rects", {}) or {}
        for ei, r in (label_rects.items() if isinstance(label_rects, dict) else []):
            try:
                if r.collidepoint(ex, ey):
                    return int(ei)
            except Exception:
                continue
        # Polyline proximity
        paths = getattr(view, "edge_paths", {}) or {}
        best_e: Optional[int] = None
        best_d = 1e9
        for ei, pts in (paths.items() if isinstance(paths, dict) else []):
            try:
                if not pts or len(pts) < 2:
                    continue
                for i in range(len(pts) - 1):
                    ax, ay = pts[i]
                    bx, by = pts[i + 1]
                    d = _dist_pt_seg(ex, ey, ax, ay, bx, by)
                    if d < best_d:
                        best_d = d
                        best_e = int(ei)
            except Exception:
                continue
        return best_e if best_d <= max(1, int(proximity_px)) else None
    except Exception:
        return None


def pick_edge_id_local(model: Any, view: Any, lx: int, ly: int, proximity_px: int = 8) -> Optional[str]:
    """Return hovered edge id from local (canvas) coordinates, if available."""
    idx = pick_edge_local(view, lx, ly, proximity_px=proximity_px)
    if idx is None:
        return None
    try:
        ids = getattr(model, "edge_id_by_index", []) or []
        edges = getattr(model, "edges", []) or []
        if len(ids) != len(edges) and hasattr(model, "rebuild_caches"):
            model.rebuild_caches()
        if 0 <= int(idx) < len(model.edge_id_by_index):
            return model.edge_id_by_index[int(idx)]
    except Exception:
        pass
    return None


def pick_edge_handle_local(view: Any, edge_index: Optional[int], lx: int, ly: int, radius: int = 8) -> Optional[str]:
    """Return 'from' or 'to' if a handle endpoint for the given edge index is hovered."""
    if edge_index is None:
        return None
    try:
        ends = (getattr(view, "edge_endpoints_local", {}) or {}).get(int(edge_index))
        if not isinstance(ends, dict):
            return None
        rad2 = int(radius) * int(radius)
        ex, ey = int(lx), int(ly)
        for side in ("from", "to"):
            p = ends.get(side)
            if not p:
                continue
            dx = ex - int(p[0])
            dy = ey - int(p[1])
            if dx * dx + dy * dy <= rad2:
                return side
    except Exception:
        return None
    return None


# Backward-compatible stubs (keep signatures used in early refactor stubs)
def hit_test_node(model: Any, view: Any, pos: Tuple[int, int]) -> Optional[str]:
    """Return node_id under the cursor. Assumes pos is in world coords."""
    try:
        wx, wy = pos
        return pick_node_id_world(model, float(wx), float(wy))
    except Exception:
        return None


def hit_test_edge(model: Any, view: Any, pos: Tuple[int, int]) -> Optional[str]:
    """Return edge_id under the cursor. Assumes pos is in local (canvas) coords."""
    try:
        lx, ly = pos
        return pick_edge_id_local(model, view, int(lx), int(ly))
    except Exception:
        return None


def hit_test_handle(model: Any, view: Any, pos: Tuple[int, int]) -> Optional[Tuple[str, str]]:
    """Return (edge_id, handle_end) like ("e42", "from") if a handle was hit.
    Assumes pos is in local (canvas) coords.
    """
    try:
        lx, ly = pos
        idx = pick_edge_local(view, int(lx), int(ly))
        if idx is None:
            return None
        end = pick_edge_handle_local(view, idx, int(lx), int(ly))
        if not end:
            return None
        # Map to id if possible
        edge_id = pick_edge_id_local(model, view, int(lx), int(ly))
        if edge_id is None:
            return None
        return edge_id, end
    except Exception:
        return None
