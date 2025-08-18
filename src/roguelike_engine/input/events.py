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
    events=None,
    *,
    diagnostics_overlay=None,
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
        # Pass through the same events list captured upstream (may be None)
        map_editor.handler.handle(camera, map, events)
    # Cache handlers and diagnostics panel
    
    kb = handle_keyboard
    ms = handle_mouse

    overlay = diagnostics_overlay
    panel = overlay.panel_rect if overlay else None
    if events is None:
        events = pygame.event.get()
    for ev in events:
        et = ev.type
        if et == pygame.QUIT:
            state.running = False
        elif et in (pygame.KEYDOWN, pygame.KEYUP):
            kb(ev, state, camera, clock, menu, entities, tiles_editor, buildings_editor, map_editor, map)
        elif et in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION):
            consumed = False
            if overlay and panel:
                # For wheel, use current mouse position; for others, prefer event.pos when available
                try:
                    mx, my = (pygame.mouse.get_pos() if et == pygame.MOUSEWHEEL else getattr(ev, 'pos', pygame.mouse.get_pos()))
                except Exception:
                    mx, my = pygame.mouse.get_pos()
                if overlay.hit_test((mx, my)):
                    consumed = bool(overlay.handle_event(ev))
            # Enable MMB panning only while an editor is active (tiles/buildings/map)
            mmb_pan_enabled = bool(active_tiles or active_buildings or active_map)
            if not consumed and not active_tiles and not active_buildings and not active_map:
                ms(ev, state, camera, clock, map, entities, mmb_pan_enabled=mmb_pan_enabled)