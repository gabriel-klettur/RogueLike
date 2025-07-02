"""
Registry para fábricas y decorador de registro.
"""
from typing import Dict, Type

_registry: Dict[str, Type] = {}


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
        raise KeyError(f"Factory '{name}' not found in registry.")
    return cls()  
