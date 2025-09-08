import pygame
from typing import Callable
from roguelike_engine.console.console_controller import ConsoleController


class ConsoleEvents:
    """
    Gestión de eventos de teclado para la consola.
    """
    def __init__(self, controller: ConsoleController):
        self.controller = controller
        # Tamaño de página para PageUp/PageDown (en líneas)
        self.page_lines = 10

    def process_event(self, event: pygame.event.Event) -> bool:
        """
        Procesa un evento de Pygame. Devuelve True si la consola manejó el evento.
        Política de captura:
        - Si la consola está abierta, se consumen TODOS los eventos de teclado (KEYDOWN/KEYUP/TEXTINPUT)
          para evitar que otras partes del juego reaccionen.
        - Cuando está cerrada, solo se intercepta la tecla de toggle y se deja pasar el resto.
        """
        # Toggle desde KEYDOWN incluso si está cerrada
        if event.type == pygame.KEYDOWN and event.key == pygame.K_BACKQUOTE:
            self.controller.toggle()
            return True

        # Si la consola NO está abierta, no consumir nada más (permite gameplay)
        if not self.controller.state.is_open:
            # Escape cuando está cerrada no se consume, para que lo maneje el juego/menú
            return False

        # A partir de aquí, la consola está ABIERTA y debemos consumir teclado
        # Consumir TEXTINPUT primero (Unicode / pegado)
        if event.type == pygame.TEXTINPUT:
            self.controller.add_text(getattr(event, 'text', ''))
            return True

        # Consumir KEYUP sin más (evita activar gameplay por suelta de tecla)
        if event.type == pygame.KEYUP:
            return True

        if event.type != pygame.KEYDOWN:
            # Otros tipos de evento no teclado: no se consumen aquí
            return False

        key = event.key
        mods = getattr(event, 'mod', 0)

        # Escape cierra la consola (y consume)
        if key == pygame.K_ESCAPE:
            self.controller.toggle()
            return True

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
        # PageUp/PageDown: scroll del historial visual
        if key == pygame.K_PAGEUP:
            self.controller.scroll_history(self.page_lines)
            return True
        if key == pygame.K_PAGEDOWN:
            self.controller.scroll_history(-self.page_lines)
            return True
        # Edición: Backspace y Delete (con o sin Ctrl)
        if key == pygame.K_BACKSPACE:
            if mods & pygame.KMOD_CTRL:
                self.controller.backspace_word()
            else:
                self.controller.backspace()
            return True
        if key == pygame.K_DELETE:
            if mods & pygame.KMOD_CTRL:
                self.controller.delete_word_forward()
            else:
                self.controller.delete_forward()
            return True
        # Movimiento del cursor
        if key == pygame.K_LEFT:
            self.controller.move_left()
            return True
        if key == pygame.K_RIGHT:
            self.controller.move_right()
            return True
        if key == pygame.K_HOME:
            self.controller.move_home()
            return True
        if key == pygame.K_END:
            self.controller.move_end()
            return True
        # Nota: No añadimos caracteres desde KEYDOWN.unicode cuando TEXTINPUT está activo
        # para evitar duplicados (TEXTINPUT ya entrega el texto). Si se detectan entornos
        # donde TEXTINPUT no llega, esta ruta podría reactivarse con una bandera.

        # Consola abierta: consumir cualquier otra tecla para que el juego no reaccione
        return True
