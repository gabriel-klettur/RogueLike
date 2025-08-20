# Monster Factory Subpackage

Este subpaquete contiene todo lo necesario para crear la entidad **monstruo** en el ECS de manera modular y escalable.

## Estructura de archivos

- `__init__.py`  
  Expone la clase `MonsterFactory` como API principal.

- `facade.py`  
  Define `MonsterFactory` (subclase de `Factory`, registrada con `@register_factory("monster")`). Orquesta:
  1. Calibración de posición (pixels/tiles)
  2. Construcción ECS mediante `MonsterBuilder`

- `builder.py`  
  Clase `MonsterBuilder` que construye la entidad monstruo con todos los componentes ECS:
  `Position`, `Sprite`, `MovementSpeed`, `Animator`, `Scale`, `Velocity`, `MultiCollider`, `ZLayer`, `Health`, `Identity`, `CombatStats`, `MeleeWeapon`, `AggroRange`, `MeleeRange`, `DamageConfig`, `PatrolRoute`, `NPCState`.

- `calibrator.py`  
  Función `calibrate_tile_position(tile_x, tile_y, monster_type)` para convertir coordenadas de tile a píxeles usando la configuración en `MONSTER_DEFS`.

- `sprite_loader.py`  
  Funciones:
  - `create_sprite_component(monster_type)`: crea componente `Sprite` y retorna imagen de muerte.
  - `create_movement_components(px, py, monster_type, cfg)`: inicializa `MovementSpeed` y `Animator`.

- `behaviour_loader.py`  
  Genera rutas de patrulla (`PatrolRoute.points`) a partir de una configuración declarativa:
  lee `data/entities/behaviour/patrols.json` (catálogo) y la propiedad `patrol` del monstruo en `data/entities/new_monsters.json`.
  Patrones soportados: `line`, `circle`, `square`, `zigzag`, `figure_eight`.

- `physics.py`  
  Funciones:
  - `calculate_position(tile_x, tile_y, cfg, sprite)`: calcula coordenadas en píxeles.
  - `create_physics_components(cfg)`: crea `Scale` y `Velocity`.
  - `create_collider_components(sprite, cfg)`: crea `MultiCollider` con `MaskCollider` y `Collider`.
  - `create_zlayer_component(cfg)`: crea `ZLayer` según configuración.

- `config.py`  
  Carga definiciones `MONSTER_DEFS` desde `data/monsters.json`.
  Expone opcionalmente `patrol` por clase de monstruo para que `builder.py` construya la ruta.

- `cache.py`  
  Maneja la carga y caching de superficies de sprites y sprites de muerte.

## Uso

```python
from roguelike_game.factories.registry import get_factory
# Crear un monstruo en tile (5,7) de tipo 'goblin'
monster_id = get_factory("monster").create(world, tile_x=5, tile_y=7, monster_type="goblin")
```

## Configuración de patrullas

- Catálogo de patrones: `data/entities/behaviour/patrols.json`
  - Define patrones y `default_params` (ej.: `circle.radius_tiles`, `square.points_per_edge`, etc.)
- Asignación por monstruo: `data/entities/new_monsters.json`
  - Cada clase puede incluir:

```json
"patrol": {
  "id": "circle",          
  "params": { "radius_tiles": 4, "points": 16, "clockwise": true }
}
```

Si se omite `patrol`, se usa una ruta lineal simple de dos puntos.
