# Path: src/roguelike_engine/input/events.py
import pygame
import roguelike_engine.config.config as config
from roguelike_engine.config.map_config import global_map_settings

from .keyboard     import handle_keyboard
from .mouse        import handle_mouse
from .continuous   import handle_continuous

def handle_events(
    state,
    camera,
    clock,
    menu,
    map,
    entities,
    effects,
    explosions,
    tiles_editor,
    buildings_editor,
    debug_overlay=None
):
    """
    Maneja eventos de pygame para input y editores.

    - Prioridad: si algún editor está activo, rutea al handler correspondiente
    - Si no, procesa eventos de juego (keyboard, mouse, continuous)
    """
    # 1) Prioridad a Tile Editor
    if tiles_editor.editor_state.active:
        tiles_editor.handler.handle(camera, map)

    # 2) Prioridad a Buildings Editor
    if buildings_editor.editor_state.active:
        buildings_editor.handler.handle(camera, entities)

    # 3) Procesar eventos de juego
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False
        
        elif event.type in (pygame.KEYDOWN, pygame.KEYUP):
            handle_keyboard(event, state, camera, clock, menu, entities, effects, tiles_editor, map)

        elif event.type in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            # Intercept overlay scroll/click when hovering debug panel
            consumed = False
            if config.DEBUG and debug_overlay:
                # get mouse pos for wheel vs click
                if event.type == pygame.MOUSEWHEEL:
                    mx, my = pygame.mouse.get_pos()
                else:
                    mx, my = event.pos
                if debug_overlay._panel_rect and debug_overlay._panel_rect.collidepoint((mx, my)):
                    debug_overlay.handle_event(event)
                    consumed = True
            if not consumed:
                handle_mouse(event, state, camera, clock, map, entities, effects, explosions)

    # Movimiento y láser continuo fuera del loop de eventos
    handle_continuous(state, camera, map, entities, menu, effects)