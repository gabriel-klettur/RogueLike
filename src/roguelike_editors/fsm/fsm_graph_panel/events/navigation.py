from __future__ import annotations

from typing import Any
from ..services import apply_zoom_at_point


def handle_navigation_event(controller: Any, model: Any, view: Any, event: Any) -> bool:
    """Handle zoom navigation events.
    Supports pygame.MOUSEWHEEL and fallback button 4/5. Keeps mouse position fixed in world space while zooming.
    Returns True if consumed.
    """
    try:
        import pygame  # type: ignore
    except Exception:
        return False

    et = getattr(event, 'type', None)
    if et not in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN):
        return False

    rect = getattr(view, 'canvas_rect', None)
    if rect is None:
        return False

    # Derive local mouse coordinates
    mouse_pos = getattr(event, 'pos', None) or pygame.mouse.get_pos()
    if not rect.collidepoint(mouse_pos):
        # Allow zoom even if cursor just outside? Keep consistent with previous handler: it required inside on wheel path.
        # Here we keep existing behavior used by toolbar path: allow wheel anywhere, but pivot still computed from mouse.
        pass
    local_x = mouse_pos[0] - rect.left
    local_y = mouse_pos[1] - rect.top

    # Determine zoom step from event
    if et == pygame.MOUSEWHEEL:
        y = int(getattr(event, 'y', 0) or 0)
        if y == 0:
            return False
    elif et == pygame.MOUSEBUTTONDOWN:
        btn = getattr(event, 'button', None)
        if btn not in (4, 5):
            return False
        y = 1 if btn == 4 else -1
    else:
        return False

    factor = (1.1) ** y
    if apply_zoom_at_point(model, float(local_x), float(local_y), factor):
        return True
    return False
