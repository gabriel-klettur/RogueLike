from __future__ import annotations
from typing import Any, Callable


def draw_edges(model: Any, surf: Any, W: Callable[[tuple[float, float]], tuple[int, int]], view: Any) -> None:
    try:
        from ._edge_renderer import draw_all_edges
    except Exception:
        return None
    return draw_all_edges(model, surf, W, view)


def redraw_hovered_edge(model: Any, surf: Any, view: Any) -> None:
    try:
        from ._hover_renderer import redraw_hovered_edge as _redraw
    except Exception:
        return None
    return _redraw(model, surf, view)


def _arrow_points(tip, direction, *, head_len=14, head_width=10):
    from ._edge_utils import arrow_points
    return arrow_points(tip, direction, head_len=head_len, head_width=head_width)
