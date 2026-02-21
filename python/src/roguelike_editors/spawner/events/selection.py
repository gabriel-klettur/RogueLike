from __future__ import annotations

from typing import Any
import pygame

from .types import EditorCtx


def handle_rmb_selection_or_clear(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """On RMB down: select building under cursor if any, else clear selection.
    Returns True if a selection was made (event consumed), False if only cleared.
    """
    try:
        ip = getattr(ctx.controller, 'instance_properties', None)
        if ip is None or not hasattr(ip, 'visuals'):
            return False
        mx, my = event.pos
        ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
        if ob is not None:
            bid = getattr(ob, 'id', None)
            if bid is not None:
                try:
                    ip.visuals.model.selected_building_id = int(bid)
                except Exception:
                    pass
                return True
        # Clicked away: clear selection, but do not consume
        try:
            ip.visuals.model.selected_building_id = None
        except Exception:
            pass
        return False
    except Exception:
        return False
