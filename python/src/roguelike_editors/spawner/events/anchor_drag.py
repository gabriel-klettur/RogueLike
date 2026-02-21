from __future__ import annotations

import pygame
from .types import EditorCtx
from roguelike_editors.spawner.services import screen_to_tile


def update_anchor_drag_motion(ctx: EditorCtx, event: pygame.event.Event) -> bool:
    """Update anchor tile while RMB-dragging a spawner instance."""
    try:
        mx, my = event.pos
        tx, ty = screen_to_tile(ctx.camera, mx, my)
        eid = getattr(ctx.model, 'dragging_eid', None)
        if eid is None:
            return False
        try:
            cfg = ctx.world.components['SpawnerConfig'][eid]
            cfg.anchor_tile = (tx, ty)
        except Exception:
            return False
        return True
    except Exception:
        return False
