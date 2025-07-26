from typing import List, Dict, Callable, Tuple, Optional


class ConsoleState:
    """
    Modelo que mantiene el estado de la consola.
    Attributes:
        history: Lista de líneas de salida.
        input_buffer: Buffer de entrada actual.
        is_open: Flag si la consola está abierta.
        cursor_pos: Posición del cursor en el buffer.
        max_lines: Máximo de líneas en el historial.
    """
    def __init__(self, max_lines: int = 200):
        self.history: List[str] = []
        self.input_buffer: str = ''
        self.is_open: bool = False
        self.cursor_pos: int = 0
        self.max_lines: int = max_lines

    def add_line(self, line: str) -> None:
        """Añade una línea al historial y mantiene el límite de max_lines."""
        self.history.append(line)
        if len(self.history) > self.max_lines:
            self.history.pop(0)


class CommandRegistry:
    """
    Registro y ejecución de comandos de consola.
    Attributes:
        commands: Mapa de nombre a función handler.
    """
    def __init__(self) -> None:
        self.commands: Dict[str, Callable[..., str]] = {}

    def register(self, name: str, handler: Callable[..., str]) -> None:
        """Registra un handler para un comando."""
        self.commands[name] = handler

    def execute(self, command_line: str) -> Tuple[str, Optional[Exception]]:
        """
        Ejecuta el comando indicado y devuelve (resultado, excepción si ocurre).
        """
        parts = command_line.strip().split()
        if not parts:
            return '', None
        name, *args = parts
        handler = self.commands.get(name)
        if handler is None:
            return f"Unknown command: {name}", None
        try:
            result = handler(*args)
            return result or '', None
        except Exception as e:
            return '', e

    def autocomplete(self, prefix: str) -> List[str]:
        """Devuelve lista de comandos que comienzan con el prefijo dado."""
        return [cmd for cmd in self.commands if cmd.startswith(prefix)]
