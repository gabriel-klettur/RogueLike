# Ejemplos de Uso del ECS

## Creación de mundo y entidad
```python
from roguelike_game.ecs.core.world import ECSWorld
from roguelike_game.ecs.components import Health, Position

world = ECSWorld()
entity = world.create_entity()
world.add_component(entity, Position(x=5, y=5))
world.add_component(entity, Health(current=10, max=10))
```

## Registro y ejecución de sistemas
```python
from roguelike_game.ecs.core.manager import ECSManager
from roguelike_game.ecs.systems.movement import MovementSystem

manager = ECSManager()
manager.register_system(MovementSystem())

# Simulación de bucle de juego
dt = 1/60
manager.update(dt)
```

## Filtrado de entidades
```python
# Obtener todas las entidades con Position y Health
entities = world.get_entities_with_components(Position, Health)
```
