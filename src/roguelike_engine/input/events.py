# Path: src/roguelike_engine/input/events.py
import pygame

from .keyboard     import handle_keyboard
from .mouse        import handle_mouse
from .continuous   import handle_continuous

def handle_events(state):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        elif event.type in (pygame.KEYDOWN, pygame.KEYUP):
            handle_keyboard(event, state)

        elif event.type in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
            handle_mouse(event, state)

    # Movimiento y laser continuo fuera del loop de eventos
    handle_continuous(state)