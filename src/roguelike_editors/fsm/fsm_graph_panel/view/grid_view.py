from __future__ import annotations

from typing import Any


def draw_grid(surface: Any, width: int, height: int, pan_x: float, pan_y: float, zoom: float, top_offset: int = 0) -> None:
    """Render an infinite-style grid that respects pan/zoom.
    - surface: pygame Surface to draw into
    - width, height: canvas size
    - pan_x, pan_y: current pan in local/canvas space
    - zoom: current zoom factor
    - top_offset: y-offset to start drawing (e.g., to avoid overlapping a toolbar)
    """
    try:
        import pygame  # type: ignore
    except Exception:
        return None
    try:
        base_grid = 40
        grid = max(8, int(base_grid * max(0.05, float(zoom))))
        grid_color = (30, 30, 34)
        # offset so grid scrolls smoothly
        ox = int(pan_x) % grid
        oy = int(pan_y) % grid
        top = int(top_offset)
        # Vertical grid lines: only draw below toolbar
        for gx in range(-ox, width, grid):
            pygame.draw.line(surface, grid_color, (gx, top), (gx, height), 1)
        # Horizontal grid lines: align with pan offset and start at first y >= top
        start_y = top + ((-oy - top) % grid)
        for gy in range(start_y, height, grid):
            pygame.draw.line(surface, grid_color, (0, gy), (width, gy), 1)
    except Exception:
        # Non-fatal rendering helper
        return None
