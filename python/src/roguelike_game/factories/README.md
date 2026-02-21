# Subpaquete `factories`

Este subpaquete define la arquitectura y el registro de **fábricas** para crear entidades en el ECS.

## base.py

Define la interfaz base para todas las fábricas:

```python
from abc import ABC, abstractmethod

class Factory(ABC):
    """Interfaz base para fábricas."""

    @abstractmethod
    def create(self, *args, **kwargs):
        """Crear entidad o componente en el mundo ECS."""
        pass
``` 

- Todas las fábricas deben heredar de `Factory` y implementar el método `create`.

## registry.py

Implementa un registro global para fábricas:

```python
_registry: dict[str, Type] = {}

def register_factory(name: str):
    """Decorador que asocia un nombre clave a una clase Factory."""
    def decorator(cls):
        _registry[name] = cls
        return cls
    return decorator


def get_factory(name: str):
    """Obtiene una instancia de la fábrica registrada bajo `name`."""
    cls = _registry.get(name)
    if not cls:
        raise KeyError(f"Factory '{name}' not found")
    return cls()
```

- `@register_factory("clave")` se usa en los módulos `facade.py` de cada subpaquete (p.ej. jugador, monstruo) para auto-registrar la fábrica.
- `get_factory("clave")` devuelve una instancia de esa fábrica, lista para invocar `create(...)`.
- El archivo `registry.py` auto-importa los `facade.py` de los subpaquetes `player` y `monster` para asegurar el registro al importar `registry`.

## Uso

```python
from roguelike_game.factories.registry import get_factory

# Crear jugador
eid = get_factory("player").create(world, tile_x=5, tile_y=7)

# Crear monstruo
eid = get_factory("monster").create(world, x=100, y=200, monster_type="goblin")
```

Con esta arquitectura, añadir nuevas fábricas es tan sencillo como crear un nuevo subpaquete con un `facade.py` que defina y registre la fábrica.
