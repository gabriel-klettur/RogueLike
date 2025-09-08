import pygame
from roguelike_engine.config.config_camera import ALLOWED_ZOOMS, next_allowed_zoom


def handle_keyboard(event, state, camera, clock, menu, entities, tiles_editor, buildings_editor, map_editor, map_manager):
    """
    Engine-level keyboard handler for generic gameplay keys.
    Global shortcuts (ESC/menu, F-keys, editor toggles, debug) are handled in
    `roguelike_game.managers.core.events.handle_events`.
    """
    if event.type != pygame.KEYDOWN:
        return

    key = event.key
    # Zoom in/out with + / - keys (main and numpad). Keep screen center stable.
    if key in (pygame.K_PLUS, pygame.K_EQUALS, pygame.K_KP_PLUS):
        direction = +1
    elif key in (pygame.K_MINUS, pygame.K_UNDERSCORE, pygame.K_KP_MINUS):
        direction = -1
    else:
        return

    try:
        # Screen center in pixels
        mx = int(getattr(camera, 'screen_width', 0)) // 2
        my = int(getattr(camera, 'screen_height', 0)) // 2
        z = float(getattr(camera, 'zoom', 1.0)) or 1.0
        # World coords under screen center before zoom
        wx = mx / z + float(getattr(camera, 'offset_x', 0.0) or 0.0)
        wy = my / z + float(getattr(camera, 'offset_y', 0.0) or 0.0)
        new_z = next_allowed_zoom(z, direction, ALLOWED_ZOOMS)
        if abs(new_z - z) < 1e-9:
            return
        camera.zoom = new_z
        # Keep the center point stable
        camera.offset_x = wx - mx / camera.zoom
        camera.offset_y = wy - my / camera.zoom
    except Exception:
        # Never break input handling on errors
        return