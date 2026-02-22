"""
Agregador de comandos de consola.

Uso recomendado desde inicialización:

from roguelike_engine.console.commands import register_commands
register_commands(registry, game)
"""
from typing import Any, Optional
from roguelike_engine.console.command_sets import register_commands as _register_all


def register_commands(registry, game: Optional[Any] = None) -> None:
    """Registra todos los comandos disponibles agrupados por dominio."""
    return _register_all(registry, game)
