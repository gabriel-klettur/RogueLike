# ECSWorld

Archivo: `src/roguelike_game/ecs/core/world.py`

## Descripción

`ECSWorld` gestiona entidades, componentes y la orquestación de sistemas.

## Métodos principales

- `__init__()` : Inicializa estructuras internas.
- `create_entity() -> Entity` : Crea y devuelve una entidad nueva.
- `add_component(entity: Entity, component: Component) -> None` : Asocia un componente a una entidad.
- `get_components(entity: Entity) -> List[Component]` : Retorna los componentes de una entidad.
- `remove_component(entity: Entity, component_type: Type[Component]) -> None` : Elimina un componente.
- `get_entities_with_components(*component_types: Type[Component]) -> List[Entity]` : Filtra entidades por tipos de componentes.
- `update(dt: float) -> None` : Invoca al `ECSManager` para ejecutar los sistemas.

## Ejemplo de uso

```python
from roguelike_game.ecs.core.world import ECSWorld
from roguelike_game.ecs.components import Position, Velocity

world = ECSWorld()
player = world.create_entity()
world.add_component(player, Position(x=0, y=0))
world.add_component(player, Velocity(dx=1, dy=0))

# En el bucle del juego
dt = 0.016
world.update(dt)
```
