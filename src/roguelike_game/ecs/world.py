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
import roguelike_engine.config.config as config
from .factories.entity_factory import spawn_monster
import pygame
import random

class NPCWorld:
    def __init__(self, screen, map_manager, buildings):
        # Referencia al mapa para colisiones
        self.map_manager = map_manager
        # Lista de edificios para colisiones y spawn
        self.buildings = buildings
        # Construir índice espacial de tiles sólidos
        self._solid_tile_index: dict[tuple[int,int], list[pygame.Rect]] = {}
        for tile in self.map_manager.solid_tiles:
            gx, gy = tile.rect.x // TILE_SIZE, tile.rect.y // TILE_SIZE
            self._solid_tile_index.setdefault((gx, gy), []).append(tile.rect)
        # Añadir colisiones de edificios al índice espacial
        for b in self.buildings:
            for cell_rect in b.collision_tiles:
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

        print(f"[ECS][Spawn] Lobby center: ({lobby_x}, {lobby_y})")
        print(f"[ECS][Spawn] Lobby size: ({zone_w}, {zone_h})")

        # Extraer posiciones caminables dentro del lobby
        solid_coords = {(tile.rect.x // TILE_SIZE, tile.rect.y // TILE_SIZE)
                       for tile in self.map_manager.solid_tiles}
        lobby_tiles = [(x, y)
                       for x in range(lobby_x, lobby_x + zone_w)
                       for y in range(lobby_y, lobby_y + zone_h)]
        walkable_tiles = [pos for pos in lobby_tiles if pos not in solid_coords]
        print(f"[ECS][Spawn] Walkable tiles in lobby: {walkable_tiles}")

        # Validación: conservar solo tiles caminables con 8 vecinos no sólidos
        valid_walkable_tiles = []
        for x, y in walkable_tiles:
            if all(((x + dx, y + dy) not in solid_coords) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if (dx, dy) != (0, 0)):
                valid_walkable_tiles.append((x, y))
        print(f"[ECS][Spawn] Valid walkable tiles (8 vecinos libres): {len(valid_walkable_tiles)}")

        building_collision_coords = {
            (rect.x // TILE_SIZE, rect.y // TILE_SIZE)
            for b in self.buildings
            for rect in b.collision_tiles
        }
        print(f"[ECS][Spawn] Building collision coords: {building_collision_coords}")

        # Filtrar spawn: tiles valid_walkable y sus 8 vecinos libres de colisión de edificios
        free_tiles = []
        for x, y in valid_walkable_tiles:
            if (x, y) not in building_collision_coords and all(((x + dx, y + dy) not in building_collision_coords) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if (dx, dy) != (0, 0)):
                free_tiles.append((x, y))
        print(f"[ECS][Spawn] Valid spawn tiles (8 vecinos libres de edificios): {len(free_tiles)}")

        # Samplear hasta 10 posiciones de spawn
        for i, (tx, ty) in enumerate(random.sample(free_tiles, min(1000, len(free_tiles)))):            
            spawn_monster(self, "barbol", tx, ty)

    def create_entity(self):
        eid = len(self.entities) + 1
        self.entities.append(eid)
        return eid



    def get_entities_with(self, *component_types):
        if not component_types:
            return
        comps = self.components
        # fast intersection of entity IDs across requested components
        sets = [set(comps.get(ct, {}).keys()) for ct in component_types]
        common = set.intersection(*sets) if sets else set()
        for eid in common:
            yield eid

    def update(self):
        # Run patrol and animation update systems
        for system in self.update_systems:
            system.update(self)

    def render(self, screen, camera):
        # Run render systems to draw entities
        for system in self.render_systems:
            system.update(self, screen, camera)
        # Debug: highlight spawn tiles in red cuando DEBUG=true
        if config.DEBUG and hasattr(self, 'spawn_tiles'):
            for tx, ty, eid in self.spawn_tiles:
                # calcular posición de pixel y tamaño con cámara
                x, y = tx * TILE_SIZE, ty * TILE_SIZE
                px, py = camera.apply((x, y))
                size = int(TILE_SIZE * camera.zoom)
                rect = pygame.Rect(px, py, size, size)
                pygame.draw.rect(screen, (255, 0, 0), rect, 2)
                # dibujar ID de NPC centrado en el rectángulo
                font_size = max(8, size // 2)
                font = pygame.font.SysFont(None, font_size)
                text_surf = font.render(str(eid), True, (255, 0, 0))
                text_rect = text_surf.get_rect(center=rect.center)
                screen.blit(text_surf, text_rect)

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
        # Indexar colisiones de edificios solo una vez
        if not getattr(self, '_building_indexed', False) and hasattr(self, 'buildings'):
            for b in self.buildings:
                for cell in b.collision_tiles:
                    gx, gy = cell.x // TILE_SIZE, cell.y // TILE_SIZE
                    self._solid_tile_index.setdefault((gx, gy), []).append(cell)
            self._building_indexed = True
        x1, y1 = rect.left // TILE_SIZE, rect.top // TILE_SIZE
        x2, y2 = rect.right // TILE_SIZE, rect.bottom // TILE_SIZE
        tiles = []
        for x in range(x1, x2 + 1):
            for y in range(y1, y2 + 1):
                tiles.extend(self._solid_tile_index.get((x, y), []))
        return tiles