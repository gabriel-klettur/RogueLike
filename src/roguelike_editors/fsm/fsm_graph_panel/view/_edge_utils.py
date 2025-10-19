from __future__ import annotations
from typing import Any, Dict, List, Tuple

Point = Tuple[float, float]
Rect = Tuple[float, float, float, float]


def build_node_pos(model: Any) -> Dict[Any, Tuple[int, int, int, int]]:
    """Build a mapping node_id -> (x, y, w, h) with ints."""
    return {
        n.get("id"): (
            int(n.get("x", 0)),
            int(n.get("y", 0)),
            int(n.get("w", 120)),
            int(n.get("h", 60)),
        )
        for n in getattr(model, "nodes", [])
    }


def dominant_side(dx: float, dy: float) -> str:
    """Return 'left'|'right'|'top'|'bottom' by dominant axis/sign."""
    if abs(dx) >= abs(dy):
        return "right" if dx >= 0 else "left"
    return "bottom" if dy >= 0 else "top"


def edge_point_with_slot(rect: Rect, toward: Point, slot_idx: int, slot_total: int) -> Point:
    """Attachment point on the rect's side, distributed by slot index."""
    x, y, w, h = rect
    cx, cy = x + w / 2.0, y + h / 2.0
    dx, dy = toward[0] - cx, toward[1] - cy
    side = dominant_side(dx, dy)
    denom = max(1, int(slot_total) + 1)
    t = (int(slot_idx) + 1) / float(denom)
    pad = 0.05
    t = pad + (1 - 2 * pad) * t
    if side == "left":
        return (x, y + h * t)
    if side == "right":
        return (x + w, y + h * t)
    if side == "top":
        return (x + w * t, y)
    return (x + w * t, y + h)


def quad_point(p0: Point, p1: Point, p2: Point, t: float) -> Point:
    it = 1.0 - t
    return (
        it * it * p0[0] + 2 * it * t * p1[0] + t * t * p2[0],
        it * it * p0[1] + 2 * it * t * p1[1] + t * t * p2[1],
    )


def bezier_samples(p0: Point, p1: Point, p2: Point, samples: int = 18) -> List[Point]:
    return [quad_point(p0, p1, p2, t / float(samples)) for t in range(samples + 1)]


def group_edges(edges: List[Dict[str, Any]], node_pos: Dict[Any, Tuple[int, int, int, int]]):
    """Compute pair counts and per-side groups for ports.

    Returns:
        pair_counts, src_groups, dst_groups
    """
    pair_counts: Dict[Tuple[Any, Any], int] = {}
    src_groups: Dict[Tuple[Any, str], List[int]] = {}
    dst_groups: Dict[Tuple[Any, str], List[int]] = {}

    for e in edges:
        fr = e.get("from")
        to = e.get("to")
        if fr is None or to is None:
            continue
        if fr == to:
            key = ("self", fr)
        else:
            key = tuple(sorted([fr, to]))  # type: ignore[assignment]
        pair_counts[key] = pair_counts.get(key, 0) + 1

    for idx, e in enumerate(edges):
        fr = e.get("from")
        to = e.get("to")
        if fr not in node_pos or to not in node_pos:
            continue
        sx, sy, sw, sh = node_pos[fr]
        tx, ty, tw, th = node_pos[to]
        sc = (sx + sw / 2.0, sy + sh / 2.0)
        tc = (tx + tw / 2.0, ty + th / 2.0)
        sdx, sdy = (tc[0] - sc[0], tc[1] - sc[1])
        ddx, ddy = (sc[0] - tc[0], sc[1] - tc[1])
        s_side = dominant_side(sdx, sdy)
        d_side = dominant_side(ddx, ddy)
        src_groups.setdefault((fr, s_side), []).append(idx)
        dst_groups.setdefault((to, d_side), []).append(idx)

    return pair_counts, src_groups, dst_groups


def arrow_points(tip: Tuple[float, float], direction: Tuple[float, float], *, head_len: int = 14, head_width: int = 10) -> List[Tuple[float, float]]:
    """Compute triangle vertices for an arrowhead."""
    import math

    vx, vy = direction
    mag = math.hypot(vx, vy) or 1.0
    ux, uy = vx / mag, vy / mag
    bx, by = tip[0] - ux * head_len, tip[1] - uy * head_len
    pxn, pyn = -uy, ux
    hw = head_width / 2.0
    left = (bx + pxn * hw, by + pyn * hw)
    right = (bx - pxn * hw, by - pyn * hw)
    return [left, right, (tip[0], tip[1])]
