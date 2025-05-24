## plan de alto nivel para un ECS mínimo que te permita respawnear un NPC con el sprite barbol_1_down.png en el centro del lobby, sin librerías externas:

0. Pre-requisitos
- Crear estructura de paquetes para el ECS:
  ```text
  src/roguelike_game/game/ecs/
  ├── __init__.py
  ├── world.py
  ├── components/
  │   ├── __init__.py
  │   ├── position.py
  │   └── sprite.py
  └── systems/
      ├── __init__.py
      └── render_system.py
  ```
- Obtener offset y tamaño del lobby usando `calculate_lobby_offset()` y `global_map_settings.zone_size` para calcular la posición central:
  ```python
  from roguelike_engine.map.utils import calculate_lobby_offset
  from roguelike_engine.config.map_config import global_map_settings

  lobby_x, lobby_y = calculate_lobby_offset()
  zone_w, zone_h = global_map_settings.zone_size
  cx = lobby_x + zone_w // 2
  cy = lobby_y + zone_h // 2
  ```

1. Definir la arquitectura ECS
- Entidad: un simple ID entero.
- Componente: clases de datos puras (sin lógica), p. ej. Position, Sprite.
- Sistema: procesos que actúan sobre entidades que tengan ciertos componentes, p. ej. RenderSystem.

2. Implementación de `src/roguelike_game/game/ecs/world.py`
```python
from .components.position import Position
from .components.sprite import Sprite
from .systems.render_system import RenderSystem
from roguelike_engine.map.utils import calculate_lobby_offset
from roguelike_engine.config.map_config import global_map_settings

class NPCWorld:
    def __init__(self, screen):
        self.screen = screen
        self.entities = []
        self.components = {
            'Position': {},
            'Sprite': {}
        }
        self.systems = [RenderSystem(screen)]

        lobby_x, lobby_y = calculate_lobby_offset()
        zone_w, zone_h = global_map_settings.zone_size
        cx = lobby_x + zone_w // 2
        cy = lobby_y + zone_h // 2

        self.spawn_npc(cx, cy)

    def create_entity(self):
        eid = len(self.entities) + 1
        self.entities.append(eid)
        return eid

    def spawn_npc(self, cx, cy):
        # Asset: barbol_1_down.png en centro del lobby
        eid = self.create_entity()
        self.components['Position'][eid] = Position(cx, cy)
        self.components['Sprite'][eid] = Sprite("assets/npc/monsters/barbol/barbol_1_down.png")

    def get_entities_with(self, *component_types):
        for eid in self.entities:
            if all(eid in self.components[ctype] for ctype in component_types):
                yield eid

    def update(self):
        pass

    def render(self):
        for system in self.systems:
            system.update(self)
```

3. Componentes mínimos
   - `src/roguelike_game/game/ecs/components/position.py`:
```python
class Position:
    def __init__(self, x: float, y: float):
        self.x = x
        self.y = y
```
   - `src/roguelike_game/game/ecs/components/sprite.py`:
```python
import pygame

class Sprite:
    def __init__(self, image_path: str):
        self.image = pygame.image.load(image_path).convert_alpha()
```

4. Sistema de render (`src/roguelike_game/game/ecs/systems/render_system.py`)
```python
class RenderSystem:
    def __init__(self, screen):
        self.screen = screen

    def update(self, world):
        for eid in world.get_entities_with('Position', 'Sprite'):
            pos = world.components['Position'][eid]
            sprite = world.components['Sprite'][eid]
            self.screen.blit(sprite.image, (pos.x, pos.y))
```

5. Integración en `src/roguelike_game/game/ecs_manager.py`
```python
from .ecs.world import NPCWorld

class ECSManager:
    def __init__(self, screen):
        self.screen = screen
        self.npc_world = NPCWorld(screen)

    def update(self, dt):
        self.npc_world.update()

    def render(self):        
        self.npc_world.render()        
```

6. Implementacion final en `src/roguelike_game/game/game.py`, `src/roguelike_game/game/render_manager.py` y `src/roguelike_game/game/update_manager.py`

---
**Integración profesional del ECS como sistema**  
- Evitar instanciar `NPCWorld` directamente en `Game.render/update`.  
- Instanciar `ECSManager` en `_init_systems`:  
```python
# en game.py
from roguelike_game.game.ecs_manager import ECSManager

class Game:
    def _init_systems(self, perf_log):
        self.systems = SystemsManager(self.state, perf_log)
        # Crear ECS y añadirlo a la lista de sistemas
        self.ecs_manager = ECSManager(self.renderer.screen)
        self.systems.add_system(self.ecs_manager)
```
- Modificar `SystemsManager` para iterar sistemas:  
```python
# en SystemsManager
def update(self, clock, screen):
    for sys in self._systems:
        sys.update(clock, screen)

def render(self, screen, camera):
    for sys in self._systems:
        sys.render(screen, camera)
```
- Así, en `update_game` y `render_game` solo se invoca `systems.update(...)` y `systems.render(...)`, incorporando el ECS de forma transparente.  
---

7. Crítica y posibles mejoras
   - Muy simple y modular.
   - Escalabilidad limitada sin pooling o event bus.
   - Optimizar consultas con archetypes o máscaras de bits en proyectos grandes.
   - Futuras extensiones: sistemas de física, IA y audio.
