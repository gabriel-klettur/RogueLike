from dataclasses import dataclass, field
from typing import List, Dict, Callable, Tuple, Optional
from roguelike_engine.console.parser import ConsoleParser


class ConsoleState:
    """
    Modelo que mantiene el estado de la consola.
    Attributes:
        history: Lista de líneas de salida.
        input_buffer: Buffer de entrada actual.
        is_open: Flag si la consola está abierta.
        cursor_pos: Posición del cursor en el buffer.
        max_lines: Máximo de líneas en el historial.
        history_scroll: Offset de scroll visual (0 = al final).
    """
    def __init__(self, max_lines: int = 200):
        self.history: List[str] = []
        self.input_buffer: str = ''
        self.is_open: bool = False
        self.cursor_pos: int = 0
        self.max_lines: int = max_lines
        self.history_scroll: int = 0

    def add_line(self, line: str) -> None:
        """Añade una línea al historial y mantiene el límite de max_lines."""
        self.history.append(line)
        if len(self.history) > self.max_lines:
            self.history.pop(0)
        # Si estamos al final (scroll=0), permanecer al final; si no, mantener scroll relativo
        # No cambiamos history_scroll aquí para respetar el desplazamiento manual del usuario.


@dataclass
class CommandMeta:
    name: str
    handler: Callable[..., str]
    usage: Optional[str] = None
    help: Optional[str] = None
    category: Optional[str] = None
    aliases: List[str] = field(default_factory=list)
    completer: Optional[Callable[[List[str]], List[str]]] = None


class CommandRegistry:
    """
    Registro y ejecución de comandos de consola con metadatos y autocompletado.
    Attributes:
        commands: mapa de nombre primario a handler.
        metas: metadatos por comando.
        alias_to_name: mapeo de alias a nombre primario.
    """
    def __init__(self) -> None:
        self.commands: Dict[str, Callable[..., str]] = {}
        self.metas: Dict[str, CommandMeta] = {}
        self.alias_to_name: Dict[str, str] = {}
        self.parser = ConsoleParser()

    def register(
        self,
        name: str,
        handler: Callable[..., str],
        *,
        usage: Optional[str] = None,
        help: Optional[str] = None,
        category: Optional[str] = None,
        aliases: Optional[List[str]] = None,
        completer: Optional[Callable[[List[str]], List[str]]] = None,
    ) -> None:
        """Registra un comando con metadatos opcionales (compatible con uso previo)."""
        self.commands[name] = handler
        meta = CommandMeta(
            name=name,
            handler=handler,
            usage=usage,
            help=help,
            category=category,
            aliases=list(aliases or []),
            completer=completer,
        )
        self.metas[name] = meta
        for alias in meta.aliases:
            self.alias_to_name[alias] = name

    def get_handler(self, name_or_alias: str) -> Optional[Callable[..., str]]:
        primary = self.alias_to_name.get(name_or_alias, name_or_alias)
        return self.commands.get(primary)

    def execute(self, command_line: str) -> Tuple[str, Optional[Exception]]:
        """Ejecuta el comando indicado y devuelve (resultado, excepción si ocurre)."""
        tokens = self.parser.tokenize(command_line)
        if not tokens:
            return '', None
        name_input, *args = tokens
        handler = self.get_handler(name_input)
        if handler is None:
            return f"Unknown command: {name_input}", None
        try:
            result = handler(*args)
            return result or '', None
        except Exception as e:
            return '', e

    def autocomplete(self, line: str) -> List[str]:
        """
        Devuelve sugerencias de autocompletado para la línea actual.
        - Si se está escribiendo el nombre del comando, sugiere comandos/aliases.
        - Si ya hay un comando válido, intenta usar el completer del comando.
        """
        ctx = self.parser.analyze(line)
        # Sin tokens: sugerir todos los comandos
        if not ctx.tokens:
            names = set(self.commands.keys()) | set(self.alias_to_name.keys())
            return sorted(names)
        # Autocompletar nombre de comando (sin espacio final)
        if len(ctx.tokens) == 1 and not ctx.ends_with_space:
            prefix = ctx.tokens[0]
            names = set(self.commands.keys()) | set(self.alias_to_name.keys())
            return sorted([n for n in names if n.startswith(prefix)])
        # Autocompletar argumentos
        cmd_input = ctx.tokens[0]
        primary = self.alias_to_name.get(cmd_input, cmd_input)
        meta = self.metas.get(primary)
        if meta and meta.completer:
            args = ctx.tokens[1:]
            if ctx.ends_with_space:
                args = args + ['']
            try:
                return meta.completer(args)
            except Exception:
                return []
        return []
