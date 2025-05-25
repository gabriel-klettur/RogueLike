from .systems.patrol_system import PatrolSystem
from .systems.movement_collision_system import MovementCollisionSystem
from .systems.animation_system import AnimationSystem
from .systems.health_bar_system import HealthBarSystem
from .systems.nameplate_system import NamePlateSystem
from .systems.collision_debug_system import CollisionDebugSystem
from .systems.death_system import DeathSystem
from .systems.death_timer_debug_system import DeathTimerDebugSystem
from .systems.death_timer_bar_system import DeathTimerBarSystem
from roguelike_engine.map.utils import calculate_lobby_offset
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from .factories.entity_factory import spawn_monster
import pygame
import random

class NPCWorld:
    def __init__(self, screen, map_manager):
        # Referencia al mapa para colisiones
        self.map_manager = map_manager
        # Construir índice espacial de tiles sólidos
        self._solid_tile_index: dict[tuple[int,int], list[pygame.Rect]] = {}
        for tile in self.map_manager.solid_tiles:
            gx, gy = tile.rect.x // TILE_SIZE, tile.rect.y // TILE_SIZE
            self._solid_tile_index.setdefault((gx, gy), []).append(tile.rect)
        # Añadir colisiones de edificios al índice espacial
        for b in getattr(self.map_manager, 'buildings', []):
            for cell_rect in getattr(b, 'collision_tiles', []):
                gx, gy = cell_rect.x // TILE_SIZE, cell_rect.y // TILE_SIZE
                self._solid_tile_index.setdefault((gx, gy), []).append(cell_rect)
        self.screen = screen
        self.entities = []
        # Components include position, sprite, patrol, movement speed, animator, health, scale and identity
        self.components = {
            'Position': {},
            'Sprite': {},
            'Patrol': {},
            'MovementSpeed': {},
            'Animator': {},
            'Health': {},
            'Scale': {},
            'Identity': {},
            'Velocity': {},
            'MultiCollider': {},
            'ZLayer': {},
            'DeathTimer': {}
        }
        # Systems: patrol, movimiento, muerte, animación y luego rendering
        self.update_systems = [PatrolSystem(), MovementCollisionSystem(), DeathSystem(), AnimationSystem()]
        self.render_systems = [HealthBarSystem(), NamePlateSystem(), CollisionDebugSystem(), DeathTimerDebugSystem(), DeathTimerBarSystem()]

        # Calculate lobby center
        lobby_x, lobby_y = calculate_lobby_offset()
        zone_w, zone_h = global_map_settings.zone_size

        # Spawn 10 'barbol' in random positions within the lobby zone
        for _ in range(10):
            tx = lobby_x + random.randint(0, zone_w - 1)
            ty = lobby_y + random.randint(0, zone_h - 1)
            spawn_monster(self, "barbol", tx, ty)

    def create_entity(self):
        eid = len(self.entities) + 1
        self.entities.append(eid)
        return eid



    def get_entities_with(self, *component_types):
        # Itera sobre el componente con menos entidades para mejorar rendimiento
        comps = self.components
        if not component_types:
            return
        smallest = min(component_types, key=lambda c: len(comps.get(c, {})))
        for eid in comps.get(smallest, {}):
            if all(eid in comps.get(ct, {}) for ct in component_types):
                yield eid

    def update(self):
        # Run patrol and animation update systems
        for system in self.update_systems:
            system.update(self)

    def render(self, screen, camera):
        # Run render systems to draw entities
        for system in self.render_systems:
            system.update(self, screen, camera)

    def remove_entity(self, eid):
        """
        Elimina la entidad y sus componentes.
        """
        if eid in self.entities:
            self.entities.remove(eid)
        for comp_dict in self.components.values():
            comp_dict.pop(eid, None)

    def get_solid_tiles_for_rect(self, rect: pygame.Rect) -> list[pygame.Rect]:
        """
        Devuelve solo los rects sólidos de tiles cercanos al área dada usando índice espacial.
        """
        x1, y1 = rect.left // TILE_SIZE, rect.top // TILE_SIZE
        x2, y2 = rect.right // TILE_SIZE, rect.bottom // TILE_SIZE
        tiles = []
        for x in range(x1, x2 + 1):
            for y in range(y1, y2 + 1):
                tiles.extend(self._solid_tile_index.get((x, y), []))
        return tiles