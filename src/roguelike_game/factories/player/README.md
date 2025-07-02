# Player Factory Subpackage

Este subpaquete contiene todo lo necesario para crear la entidad jugador en el ECS de manera modular y escalable.

## Estructura de archivos

- `__init__.py`  
  Expone la clase `PlayerFactory` como API principal.

- `facade.py`  
  Define `PlayerFactory` (subclase de `Factory`, registrada con `@register_factory("player")`). Orquesta:
  1. Carga de sprites
  2. Calibración de posición (pixels/tiles)
  3. Construcción ECS

- `builder.py`  
  Clase `PlayerBuilder`: construye la entidad y añade todos los componentes ECS (Position, Sprite, Animator, Health, CombatStats, MeleeWeapon, TrailComponent, FSM, etc.).

- `calibrator.py`  
  Función `calibrate_tile_position(tile_x, tile_y, initial_frame)` para convertir coordenadas de tile a píxeles alineando el collider de "feet" al centro del tile.

- `loader.py`  
  Funciones:
  - `load_and_scale_sprites(class_player)`: carga y escala sprites según configuración.
  - `extract_initial_frame(sprites_dict)`: obtiene el primer frame para inicializar `Sprite`.
  - `build_animator_map(sprites_dict)`: crea un diccionario plano de animaciones para `Animator`.

- `config.py`  
  Lee valores y constantes desde `data/players.json`:
  - Tamaños (`ORIGINAL_SPRITE_SIZE`, `RENDERED_SPRITE_SIZE`)
  - Estadísticas de jugador (`PLAYER_STATS`, `DEFAULT_SPEED`, etc.)
  - Configuración de arma cuerpo a cuerpo y trail.

- `collider.py`  
  Función `create_body_and_feet(surface)` para generar colisiones de cuerpo (mask) y pies (rect).

- `assets/`  
  Recursos de sprites e imágenes.

## Integración con el Registry

Para instanciar la fábrica sin importar directamente el módulo:
```python
from roguelike_game.factories.registry import get_factory
factory = get_factory("player")
``` 

## Ejemplos de uso

### Creación en coordenadas absolutas (píxeles)
```python
eid = factory.create(
    world,
    x=100,
    y=200,
    class_player="wizard"
)
```

### Creación usando coordenadas de tiles
```python
eid = factory.create(
    world,
    tile_x=5,
    tile_y=8,
    class_player="knight"
)
```

## Testing

Se recomienda crear tests en `tests/factories/player/` para cada módulo:
- Verificar carga y escala de sprites.
- Calibración de tile → píxeles.
- Builder añade todos los componentes esperados.
- Facade valida args y delega correctamente.

## Buenas prácticas y escalabilidad

- **Responsabilidad única**: cada módulo cumple un rol claro.
- **Open/Closed**: nuevas clases de jugador se añaden modificando solo `data/players.json` y creando assets.
- **Registro dinámico**: evita imports cruzados y facilita plugins.
- **Configuración externa**: ajusta stats y recursos sin tocar código.
- **Type hints y linters**: mejora calidad y mantenibilidad.

---

_Documentación generada automáticamente para el subpaquete `player` en `roguelike_game.factories`._
