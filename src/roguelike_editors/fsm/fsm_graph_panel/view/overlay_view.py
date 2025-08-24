from __future__ import annotations
from typing import Any, Callable


def draw_edge_handles_and_preview(model: Any, surf: Any, W: Callable[[tuple[float, float]], tuple[int, int]], view: Any) -> None:
    try:
        import pygame  # type: ignore
        import math
    except Exception:
        return None
    try:
        # Draw handle circles for hovered edge or currently dragging edge
        hovered_e_idx = getattr(model, 'hover_edge_index', None)
        hovered_e_id = getattr(model, 'hover_edge_id', None)
        dragging_e_idx = getattr(model, 'dragging_edge_index', None)
        dragging_e_id = getattr(model, 'dragging_edge_id', None)
        hovered_end = getattr(model, 'hover_edge_handle_end', None)

        def _draw_handle(center, filled=False, radius=6):
            cx, cy = int(center[0]), int(center[1])
            color = (255, 230, 120)
            if filled:
                pygame.draw.circle(surf, color, (cx, cy), radius)
                pygame.draw.circle(surf, (40, 40, 44), (cx, cy), radius-2)
                pygame.draw.circle(surf, color, (cx, cy), radius, 2)
            else:
                pygame.draw.circle(surf, color, (cx, cy), radius, 2)

        # Draw for hovered edge
        ends = None
        if isinstance(hovered_e_id, str):
            ends = view.edge_endpoints_local.get(hovered_e_id)
        if ends is None and hovered_e_idx is not None:
            try:
                ends = view.edge_endpoints_local.get(int(hovered_e_idx))
            except Exception:
                ends = None
        if isinstance(ends, dict):
            fr = ends.get('from'); to = ends.get('to')
            if fr:
                _draw_handle(fr, filled=(hovered_end == 'from'))
            if to:
                _draw_handle(to, filled=(hovered_end == 'to'))

        # Draw for dragging edge (always show both handles on that edge)
        ends = None
        if isinstance(dragging_e_id, str):
            ends = view.edge_endpoints_local.get(dragging_e_id)
        if ends is None and dragging_e_idx is not None:
            try:
                ends = view.edge_endpoints_local.get(int(dragging_e_idx))
            except Exception:
                ends = None
        if isinstance(ends, dict):
            fr = ends.get('from'); to = ends.get('to')
            if fr:
                _draw_handle(fr, filled=(getattr(model, 'dragging_edge_end', None) == 'from'))
            if to:
                _draw_handle(to, filled=(getattr(model, 'dragging_edge_end', None) == 'to'))

        # Drag preview: show arrow pointing toward the 'to' end
        if dragging_e_idx is not None or isinstance(dragging_e_id, str):
            end_side = getattr(model, 'dragging_edge_end', None)
            px = getattr(model, 'dragging_edge_preview_x', None)
            py = getattr(model, 'dragging_edge_preview_y', None)
            ends = None
            if isinstance(dragging_e_id, str):
                ends = view.edge_endpoints_local.get(dragging_e_id)
            if ends is None and dragging_e_idx is not None:
                try:
                    ends = view.edge_endpoints_local.get(int(dragging_e_idx))
                except Exception:
                    ends = None
            if end_side in ('from', 'to') and isinstance(px, (int, float)) and isinstance(py, (int, float)) and isinstance(ends, dict):
                tip_local = W((float(px), float(py)))
                fixed_local = ends.get('to' if end_side == 'from' else 'from')
                if fixed_local and tip_local:
                    # Determine start (source) and dest (arrowhead) so that arrow always points to 'to'
                    if end_side == 'from':
                        sx, sy = int(tip_local[0]), int(tip_local[1])     # moving 'from'
                        dx, dy = int(fixed_local[0]), int(fixed_local[1]) # fixed 'to'
                    else:  # dragging 'to'
                        sx, sy = int(fixed_local[0]), int(fixed_local[1]) # fixed 'from'
                        dx, dy = int(tip_local[0]), int(tip_local[1])     # moving 'to'
                    # Draw preview polyline and arrowhead at dest
                    pygame.draw.line(surf, (255, 230, 120), (sx, sy), (dx, dy), 2)
                    vx, vy = (dx - sx), (dy - sy)
                    mag = math.hypot(vx, vy) or 1.0
                    ux, uy = vx / mag, vy / mag
                    head_len = 14
                    head_width = 10
                    bx, by = dx - ux * head_len, dy - uy * head_len
                    pxn, pyn = -uy, ux
                    hw = head_width / 2.0
                    left = (bx + pxn * hw, by + pyn * hw)
                    right = (bx - pxn * hw, by - pyn * hw)
                    pygame.draw.polygon(surf, (255, 230, 120), [left, right, (dx, dy)])
    except Exception:
        pass
