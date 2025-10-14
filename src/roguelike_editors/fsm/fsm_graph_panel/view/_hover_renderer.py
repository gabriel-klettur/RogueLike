from __future__ import annotations
from typing import Any, Tuple

try:
    import pygame  # type: ignore
    import math
except Exception:  # pragma: no cover
    pygame = None  # type: ignore
    math = None  # type: ignore

from ._edge_utils import arrow_points


def redraw_hovered_edge(model: Any, surf: Any, view: Any) -> None:
    """Re-draw the hovered edge on top using cached path and label rects.

    Relies on view.edge_paths and view.edge_label_rects filled by the main draw pass.
    """
    if pygame is None or math is None:
        return None
    try:
        hover_eid = getattr(model, "hover_edge_id", None)
        hover_ei = getattr(model, "hover_edge_index", None)
        key = None
        if isinstance(hover_eid, str) and hover_eid in getattr(view, "edge_paths", {}):
            key = hover_eid
        else:
            try:
                if hover_ei is not None and int(hover_ei) in getattr(view, "edge_paths", {}):
                    key = int(hover_ei)
            except Exception:
                key = None
        if key is None:
            return None
        pts = view.edge_paths.get(key)
        if not (isinstance(pts, list) and len(pts) >= 2):
            return None
        # Find the edge dict and compute style
        idx = None
        if isinstance(key, int):
            idx = key
        elif isinstance(key, str):
            try:
                idx = (getattr(model, "edge_index_by_id", {}) or {}).get(key)
            except Exception:
                idx = None
        e = None
        edges = getattr(model, "edges", [])
        if isinstance(idx, int) and 0 <= idx < len(edges):
            e = edges[idx]
        elif isinstance(key, str):
            try:
                for i, ee in enumerate(edges):
                    if ee.get("id") == key:
                        idx = i
                        e = ee
                        break
            except Exception:
                e = None
        if not isinstance(e, dict):
            return None
        eid = e.get("id")
        is_edge_selected = (idx == getattr(model, "selected_edge_index", None)) or (
            isinstance(eid, str) and eid == getattr(model, "selected_edge_id", None)
        )
        color = e.get("color", (120, 120, 140))
        if e.get("active"):
            color = (255, 210, 90)
        elif is_edge_selected:
            color = (255, 220, 110)
        else:
            color = (255, 230, 120)
        width = int(e.get("width", 2))
        if is_edge_selected:
            width = max(width + 2, 4)
        else:
            width = max(width + 1, 3)
        head_len = int(e.get("head_len", 14))
        head_width = int(e.get("head_width", 10))

        p_tip = pts[-1]
        p_prev = pts[-2]
        if len(pts) >= 3:
            pygame.draw.lines(surf, color, False, pts[:-1], width)
        else:
            vx, vy = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
            mag = math.hypot(vx, vy) or 1.0
            retract = 0.001 * mag
            ux, uy = vx / mag, vy / mag
            shortened_tip = (p_tip[0] - ux * retract, p_tip[1] - uy * retract)
            pygame.draw.lines(surf, color, False, [pts[0], shortened_tip], width)
        dir_vec = (p_tip[0] - p_prev[0], p_tip[1] - p_prev[1])
        pygame.draw.polygon(surf, color, arrow_points(p_tip, dir_vec, head_len=head_len, head_width=head_width))

        is_editing = (getattr(model, "editing_edge_index", None) == idx) or (
            isinstance(eid, str) and getattr(model, "editing_edge_id", None) == eid
        )
        if is_editing:
            return None
        label = e.get("label") or e.get("on") or e.get("event")
        if not label:
            return None
        try:
            lr = view.edge_label_rects.get(key)
            if lr is None and isinstance(idx, int):
                lr = view.edge_label_rects.get(idx)
        except Exception:
            lr = None
        if lr is not None:
            font = pygame.font.SysFont(None, 20 if is_edge_selected else 20)
            txt = font.render(str(label), True, (255, 230, 120))
            tr = txt.get_rect(center=(lr.centerx, lr.centery))
            surf.blit(txt, tr)
    except Exception:
        pass
