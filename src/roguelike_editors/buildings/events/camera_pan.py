import logging
import pygame

logger = logging.getLogger("building_editor.events")


def handle_pan_state(owner, ev, camera) -> bool:
    """Handle middle-mouse panning lifecycle. Mutates owner.panning and camera offsets.

    Returns True if the event was consumed.
    """
    if ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, "button", None) == 2:
        owner.panning = True
        owner.pan_start = getattr(ev, "pos", (0, 0))
        owner.pan_offset_start = (camera.offset_x, camera.offset_y)
        logger.info(f" EDITOR] Start panning at {owner.pan_start}, offset_start={owner.pan_offset_start}")
        return True
    if ev.type == pygame.MOUSEBUTTONUP and getattr(ev, "button", None) == 2 and getattr(owner, "panning", False):
        owner.panning = False
        logger.info(" EDITOR] Stop panning")
        return True
    if ev.type == pygame.MOUSEMOTION and getattr(owner, "panning", False):
        rel_x, rel_y = getattr(ev, "rel", (0, 0))
        camera.offset_x -= rel_x / camera.zoom
        camera.offset_y -= rel_y / camera.zoom
        return True
    return False
