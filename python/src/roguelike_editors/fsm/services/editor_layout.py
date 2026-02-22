from __future__ import annotations

from typing import Tuple, Optional


def clamp_anchor(ax: int, ay: int, screen_size: Tuple[int, int], panel_size: Tuple[int, int]) -> Tuple[int, int]:
    sw, sh = int(screen_size[0]), int(screen_size[1])
    pw, ph = int(panel_size[0]), int(panel_size[1])
    ax = max(4, min(ax, max(4, sw - pw - 4)))
    ay = max(4, min(ay, max(4, sh - ph - 4)))
    return ax, ay


def compute_panel_anchor_next_to_toolbar(
    toolbar_rect: Optional[object],
    screen_size: Tuple[int, int],
    panel_size: Tuple[int, int],
    *,
    default_anchor: Tuple[int, int] = (20, 120),
    margin: int = 8,
) -> Tuple[int, int]:
    """Anchor a floating panel to the right of the toolbar, clamped to screen."""
    anchor = default_anchor
    if toolbar_rect is not None:
        try:
            ax = int(toolbar_rect.right) + int(margin)
            ay = int(toolbar_rect.top)
            anchor = clamp_anchor(ax, ay, screen_size, panel_size)
        except Exception:
            pass
    return anchor


def compute_graph_canvas_anchor(
    sets_rect: Optional[object],
    screen_size: Tuple[int, int],
    canvas_size: Tuple[int, int] = (800, 520),
    *,
    default_anchor: Tuple[int, int] = (360, 120),
    margin: int = 8,
) -> Tuple[int, int]:
    """Anchor the graph canvas to the right of the sets panel, clamped to screen."""
    anchor = default_anchor
    if sets_rect is not None:
        try:
            ax = int(sets_rect.right) + int(margin)
            ay = int(sets_rect.top)
            anchor = clamp_anchor(ax, ay, screen_size, canvas_size)
        except Exception:
            pass
    return anchor
