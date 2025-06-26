# ECSManager

Archivo: `src/roguelike_game/ecs/core/manager.py`

## Descripción

`ECSManager` se encarga de registrar, gestionar y ejecutar los sistemas en el bucle principal.

## Métodos principales

- `__init__()` : Inicializa el manager sin sistemas.
- `register_system(system: System) -> None` : Añade un sistema al manager.
- `unregister_system(system_type: Type[System]) -> None` : Elimina un sistema registrado.
- `update(dt: float) -> None` : Ejecuta cada sistema con el delta de tiempo.

## Ejemplo de uso

```python
from roguelike_game.ecs.core.manager import ECSManager

manager = ECSManager()
manager.register_system(MovementSystem())
manager.update(0.016)
```
