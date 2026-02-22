"""
Agregador de conjuntos de comandos (command sets) por dominio.
"""
from typing import Any, Optional
from .core import register_core_commands
from .inventory import register_inventory_commands


def register_commands(registry, game: Optional[Any] = None) -> None:
    """Registra todos los comandos disponibles agrupados por dominio."""
    register_core_commands(registry, game)
    register_inventory_commands(registry, game)
