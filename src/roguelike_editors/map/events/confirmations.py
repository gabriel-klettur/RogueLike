from __future__ import annotations

import logging
import pygame

from roguelike_editors.map.commands.paint_tiles_command import PaintTilesCommand

logger = logging.getLogger(__name__)


def handle_confirmation_dialogs(
    ev: pygame.event.Event, state, controller, manager, map_manager
) -> bool:
    # Delete zone
    if state.confirm_delete_zone:
        if controller.toolbar.delete_zone.events.handle_confirm_click(ev.pos):
            return True

    # Paint tiles confirm
    if state.confirm_paint_tiles:
        zone = state.pending_paint_tiles_zone
        if state.confirm_paint_yes_rect and state.confirm_paint_yes_rect.collidepoint(ev.pos):
            # Use the overlay code selected via the Tile Picker (already set in state.tile_code)
            # Fallback: if not set, keep current value as-is
            tiles = map_manager.tiles_by_zone.get(zone, [])
            try:
                setattr(state, "tutorial_paint_tiles_confirmed_pulse", True)
            except Exception:
                pass
            state.begin_async_tool("paint_tiles", zone, tiles)
            state.current_command = PaintTilesCommand(zone, state.tile_code)
            try:
                map_manager.view.invalidate_cache()
            except Exception:
                pass
            logger.info(
                f"[MapEditor] Paint tiles confirmed zone={zone} count={len(tiles)} overlay={state.tile_code}"
            )
            state.reset_paint_tiles_dialog()
            return True

        if state.confirm_paint_no_rect and state.confirm_paint_no_rect.collidepoint(ev.pos):
            logger.info("[MapEditor] Paint tiles canceled")
            state.reset_paint_tiles_dialog()
            return True

    # Clear colliders
    if state.confirm_clear_colliders:
        if controller.toolbar.clear_colliders.events.handle_confirm_click(ev.pos):
            return True

    # Paint colliders
    if state.confirm_paint_colliders:
        if controller.toolbar.paint_colliders.events.handle_confirm_click(ev.pos):
            return True

    # Add zone
    if state.confirm_add_zone:
        if controller.toolbar.add_zone.events.handle_confirm_click(ev.pos):
            return True

    return False
