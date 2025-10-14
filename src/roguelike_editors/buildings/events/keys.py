import logging
import pygame
from typing import Callable

logger = logging.getLogger("building_editor.events")


def handle_keydown(owner, editor, controller, state, ev, camera, entities, save_fn: Callable) -> bool:
    """Handle KEYDOWN actions. Returns True if the handler should return early.

    Actions mirror the legacy implementation to keep test expectations.
    """
    # Ctrl+P (or P) -> toggle picker (tests use plain P)
    if ev.key == pygame.K_p:
        controller.toggle_picker()
        return True

    # ESC -> Close editor and save
    if ev.key == pygame.K_ESCAPE:
        logger.info("Escape: closing Building Editor and saving")
        editor.active = False
        editor.selected_building = None
        editor.dragging = False
        editor.resizing = False
        editor.split_dragging = False
        try:
            save_fn(
                entities.buildings,
                z_state=state.z_state,
                zone_offsets=getattr(owner, "zone_offsets", None),
            )
        except Exception:
            pass
        return True

    # Ctrl+S -> save without closing
    if ev.key == pygame.K_s and (ev.mod & pygame.KMOD_CTRL):
        logger.info("Ctrl+S: saving buildings")
        try:
            save_fn(
                entities.buildings,
                z_state=state.z_state,
                zone_offsets=getattr(owner, "zone_offsets", None),
            )
        except Exception:
            pass
        return True

    # Ctrl+Z -> undo delete
    if ev.key == pygame.K_z and (ev.mod & pygame.KMOD_CTRL):
        owner._undo_delete(entities.buildings)
        return True

    # R -> start resize only on active_building (not in colliders mode)
    if ev.key == pygame.K_r and not getattr(editor, "colliders_mode", False):
        ab = getattr(editor, "active_building", None)
        if ab is not None:
            try:
                mouse_pos = pygame.mouse.get_pos()
            except Exception:
                mouse_pos = (0, 0)
            controller._start_resize(ab, mouse_pos)
        return True

    # N -> place random building without picker (not in colliders mode)
    if ev.key == pygame.K_n and not getattr(editor, "colliders_mode", False):
        controller.placer_tool.place_building_at_mouse(entities.buildings)
        return True

    # DELETE -> delete only active_building (not in colliders mode)
    if ev.key == pygame.K_DELETE and not getattr(editor, "colliders_mode", False):
        ab = getattr(editor, "active_building", None)
        if ab is not None:
            if getattr(ab, "id", None) is None:
                controller._delete_building(ab, entities.buildings)
            else:
                logger.info("⌫ Supr: confirmar eliminación de edificio activo")
                controller._ask_confirm_delete(ab)
        return True

    # D -> reset only on active_building (not in colliders mode)
    if ev.key == pygame.K_d and not getattr(editor, "colliders_mode", False):
        ab = getattr(editor, "active_building", None)
        if ab is not None:
            try:
                controller.default_tool.apply_reset(ab)
            except Exception:
                pass
        return True

    return False


def handle_keyup(editor, ev) -> bool:
    """Handle KEYUP transitions like finishing resize. Returns True if consumed."""
    if ev.type == pygame.KEYUP and ev.key == pygame.K_r:
        if getattr(editor, "resizing", False):
            editor.resizing = False
            logger.info("✅ Resize finalizado al soltar R")
        return True
    return False
