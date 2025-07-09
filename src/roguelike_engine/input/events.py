# Path: src/roguelike_engine/input/events.py
import pygame

from .keyboard     import handle_keyboard
from .mouse        import handle_mouse


def handle_events(
    state,
    camera,
    clock,
    menu,
    map,
    entities,    
    tiles_editor,
    buildings_editor,
    map_editor,
    debug_overlay=None
):
    """
    Maneja eventos de pygame para input y editores.

    - Prioridad: si algún editor está activo, rutea al handler correspondiente
    - Si no, procesa eventos de juego (keyboard, mouse, continuous)
    """
    # Optimized event handling
    active_tiles = tiles_editor.editor_state.active
    active_buildings = buildings_editor.editor_state.active
    active_map = False
    try:
        active_map = map_editor.editor_state.active
    except Exception:
        pass
    # Pre-handle editors
    if active_tiles:
        tiles_editor.handler.handle(camera, map)
    elif active_buildings:
        buildings_editor.handler.handle(camera, entities)
    elif active_map:
        map_editor.handler.handle(camera, map)
    # Cache handlers and debug panel
    
    kb = handle_keyboard
    ms = handle_mouse

    panel = debug_overlay._panel_rect if debug_overlay else None
    events = pygame.event.get()
    for ev in events:
        et = ev.type
        if et == pygame.QUIT:
            state.running = False
        elif et in (pygame.KEYDOWN, pygame.KEYUP):
            kb(ev, state, camera, clock, menu, entities, tiles_editor, buildings_editor, map_editor, map)
        elif et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            consumed = False
            if panel:
                mx, my = (pygame.mouse.get_pos() if et == pygame.MOUSEWHEEL else ev.pos)
                if panel.collidepoint((mx, my)):
                    debug_overlay.handle_event(ev)
                    consumed = True
            if not consumed and not active_tiles and not active_buildings:
                ms(ev, state, camera, clock, map, entities)