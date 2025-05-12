# Path: src/roguelike_engine/input/events.py

import pygame

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
    buildings_editor
):
    """
    Maneja eventos de pygame para input y editores.

    - Prioridad: si algún editor está activo, rutea al handler correspondiente
    - Si no, procesa eventos de juego (keyboard, mouse, continuous)
    """
    # 1) Prioridad a Tile Editor
    if tiles_editor.editor_state.active:
        tiles_editor.handler.handle(camera, map)
        return

    # 2) Prioridad a Buildings Editor
    if buildings_editor.editor_state.active:
        buildings_editor.handler.handle(camera, entities)
        return

    # 3) Procesar eventos de juego
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        elif event.type in (pygame.KEYDOWN, pygame.KEYUP):
            handle_keyboard(event, state, camera, clock, menu, entities, effects, tiles_editor)

        elif event.type in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            handle_mouse(event, state, camera, clock, map, entities, effects, explosions)

    # Movimiento y láser continuo fuera del loop de eventos
    handle_continuous(state, camera, map, entities, menu, effects)
