# Path: src/roguelike_engine/input/events.py
import pygame

from .keyboard     import handle_keyboard
from .mouse        import handle_mouse
from .continuous   import handle_continuous

def handle_events(state, camera, clock, menu, map, entities, effects, explosions, tiles_editor):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        elif event.type in (pygame.KEYDOWN, pygame.KEYUP):
            handle_keyboard(event, state, camera, clock, menu, entities, effects, tiles_editor)

        elif event.type in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            handle_mouse(event, state, camera, clock, map, entities, effects, explosions)

    # Movimiento y laser continuo fuera del loop de eventos
    handle_continuous(state, camera, map, entities, menu, effects)