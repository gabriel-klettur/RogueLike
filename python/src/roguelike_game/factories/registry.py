"""
Registry para fábricas y decorador de registro.
"""
from typing import Dict, Type
import importlib
import sys

_registry: Dict[str, Type] = {}


def _try_load(name: str) -> None:
    mod_name = f"roguelike_game.factories.{name}.facade"
    if mod_name in sys.modules:
        importlib.reload(sys.modules[mod_name])
    else:
        importlib.import_module(mod_name)


def register_factory(name: str):
    """Decorador para registrar una fábrica con un nombre clave."""
    def decorator(cls):
        _registry[name] = cls
        return cls
    return decorator


def get_factory(name: str):
    """Obtener una instancia de la fábrica registrada por nombre."""
    cls = _registry.get(name)
    if not cls:
        _try_load(name)
        cls = _registry.get(name)
    if not cls:
        raise KeyError(f"Factory '{name}' not found in registry.")
    return cls()


