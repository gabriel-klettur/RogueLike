import os
from typing import List, Optional
from roguelike_engine.console.model.model import ConsoleState, CommandRegistry


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
        prefix = self.state.input_buffer
        options = self.registry.autocomplete(prefix)
        if options:
            common = os.path.commonprefix(options)
            self.state.input_buffer = common
            self.state.cursor_pos = len(common)

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

    def add_char(self, char: str) -> None:
        """Añade un carácter en la posición del cursor."""
        buf = self.state.input_buffer
        pos = self.state.cursor_pos
        self.state.input_buffer = buf[:pos] + char + buf[pos:]
        self.state.cursor_pos += 1
