import os
import pygame
from typing import List, Optional
from roguelike_engine.console.console_model import ConsoleState, CommandRegistry


class ConsoleController:
    """
    Controlador que gestiona la lógica de la consola.
    """
    def __init__(self, state: ConsoleState, registry: CommandRegistry):
        self.state = state
        self.registry = registry
        self.command_history: List[str] = []
        self.history_index: Optional[int] = None

    def toggle(self) -> None:
        """Abre o cierra la consola."""
        self.state.is_open = not self.state.is_open
        # Gestionar modo de entrada de texto de Pygame (para TEXTINPUT)
        try:
            if self.state.is_open:
                pygame.key.start_text_input()
            else:
                pygame.key.stop_text_input()
        except Exception:
            # En algunos entornos puede no estar disponible; ignorar con seguridad
            pass

    def submit(self) -> None:
        """Envía el comando actual para su ejecución."""
        cmd = self.state.input_buffer.strip()
        if not cmd:
            return
        # Añadir a historial de comandos
        self.command_history.append(cmd)
        self.history_index = None
        # Mostrar comando en consola
        self.state.add_line(f"> {cmd}")
        # Ejecutar
        output, exc = self.registry.execute(cmd)
        if exc:
            self.state.add_line(f"Error: {exc}")
        elif output:
            for line in str(output).splitlines():
                self.state.add_line(line)
        # Reset buffer
        self.state.input_buffer = ''
        self.state.cursor_pos = 0

    def autocomplete(self) -> None:
        """Autocompleta el texto actual en el buffer."""
        line = self.state.input_buffer
        options = self.registry.autocomplete(line)
        if options:
            common = os.path.commonprefix(options)
            # Si hay coincidencia común, la aplicamos. Si no, usamos la primera sugerencia.
            new_text = common or options[0]
            self.state.input_buffer = new_text
            self.state.cursor_pos = len(new_text)

    def navigate_history(self, up: bool) -> None:
        """Navega por el historial de comandos."""
        if not self.command_history:
            return
        if self.history_index is None:
            # Empieza desde el final
            self.history_index = len(self.command_history) - 1
        else:
            self.history_index += -1 if up else 1
            # Limitar rango
            self.history_index = max(0, min(self.history_index, len(self.command_history) - 1))
        # Actualizar buffer
        self.state.input_buffer = self.command_history[self.history_index]
        self.state.cursor_pos = len(self.state.input_buffer)

    def backspace(self) -> None:
        """Elimina el carácter antes del cursor."""
        if self.state.cursor_pos > 0:
            buf = self.state.input_buffer
            pos = self.state.cursor_pos
            self.state.input_buffer = buf[:pos-1] + buf[pos:]
            self.state.cursor_pos -= 1

    def delete_forward(self) -> None:
        """Elimina el carácter en la posición del cursor (DEL)."""
        buf = self.state.input_buffer
        pos = self.state.cursor_pos
        if pos < len(buf):
            self.state.input_buffer = buf[:pos] + buf[pos+1:]

    def backspace_word(self) -> None:
        """Elimina la palabra anterior al cursor (Ctrl+Backspace)."""
        buf = self.state.input_buffer
        pos = self.state.cursor_pos
        if pos == 0:
            return
        # Saltar espacios a la izquierda
        i = pos - 1
        while i >= 0 and buf[i].isspace():
            i -= 1
        # Saltar caracteres de palabra
        while i >= 0 and not buf[i].isspace():
            i -= 1
        new_pos = i + 1
        self.state.input_buffer = buf[:new_pos] + buf[pos:]
        self.state.cursor_pos = new_pos

    def delete_word_forward(self) -> None:
        """Elimina la palabra siguiente al cursor (Ctrl+Delete)."""
        buf = self.state.input_buffer
        pos = self.state.cursor_pos
        n = len(buf)
        if pos >= n:
            return
        # Saltar espacios a la derecha
        i = pos
        while i < n and buf[i].isspace():
            i += 1
        # Saltar caracteres de palabra
        j = i
        while j < n and not buf[j].isspace():
            j += 1
        self.state.input_buffer = buf[:pos] + buf[j:]

    def move_left(self) -> None:
        if self.state.cursor_pos > 0:
            self.state.cursor_pos -= 1

    def move_right(self) -> None:
        if self.state.cursor_pos < len(self.state.input_buffer):
            self.state.cursor_pos += 1

    def move_home(self) -> None:
        self.state.cursor_pos = 0

    def move_end(self) -> None:
        self.state.cursor_pos = len(self.state.input_buffer)

    def add_char(self, char: str) -> None:
        """Añade un carácter en la posición del cursor."""
        buf = self.state.input_buffer
        pos = self.state.cursor_pos
        self.state.input_buffer = buf[:pos] + char + buf[pos:]
        self.state.cursor_pos += 1

    def add_text(self, text: str) -> None:
        """Añade texto (posiblemente multicaracter, desde TEXTINPUT)."""
        if not text:
            return
        buf = self.state.input_buffer
        pos = self.state.cursor_pos
        self.state.input_buffer = buf[:pos] + text + buf[pos:]
        self.state.cursor_pos += len(text)

    def scroll_history(self, delta_lines: int) -> None:
        """Desplaza el historial visualmente (PageUp/Down)."""
        total = len(self.state.history)
        if total == 0:
            self.state.history_scroll = 0
            return
        new_scroll = self.state.history_scroll + delta_lines
        # Clamp: 0 = fondo (más reciente). Máximo = total-1 (top)
        new_scroll = max(0, min(new_scroll, max(0, total - 1)))
        self.state.history_scroll = new_scroll
