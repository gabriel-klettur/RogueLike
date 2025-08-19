import pygame
from typing import Callable
from roguelike_engine.console.controller.controller import ConsoleController


class ConsoleEvents:
    """
    Gestión de eventos de teclado para la consola.
    """
    def __init__(self, controller: ConsoleController):
        self.controller = controller

    def process_event(self, event: pygame.event.Event) -> bool:
        """
        Procesa un evento de Pygame. Devuelve True si la consola manejó el evento.
        """
        if event.type != pygame.KEYDOWN:
            return False

        key = event.key
        # Toggle consola
        if key == pygame.K_BACKQUOTE:
            self.controller.toggle()
            return True
        # Escape: solo cierra si la consola ya está abierta; no la abre.
        if key == pygame.K_ESCAPE:
            if self.controller.state.is_open:
                self.controller.toggle()
                return True
            # Si está cerrada, no consumir el evento para permitir manejo global (menú, etc.)
            return False
        # Solo procesar si la consola está abierta
        if not self.controller.state.is_open:
            return False
        # Enter: ejecutar comando
        if key == pygame.K_RETURN:
            self.controller.submit()
            return True
        # Autocomplete
        if key == pygame.K_TAB:
            self.controller.autocomplete()
            return True
        # Historial
        if key == pygame.K_UP:
            self.controller.navigate_history(up=True)
            return True
        if key == pygame.K_DOWN:
            self.controller.navigate_history(up=False)
            return True
        # Backspace
        if key == pygame.K_BACKSPACE:
            self.controller.backspace()
            return True
        # Carácter imprimible
        if event.unicode and len(event.unicode) == 1:
            self.controller.add_char(event.unicode)
            return True
        return False
