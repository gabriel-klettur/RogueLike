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
        """Inicializa NPCWorld: configura índice espacial, componentes, sistemas y spawn inicial."""
        self.screen = screen
        self.map_manager = map_manager
        self.buildings = buildings
        self.entities = []
        self._setup_spatial_index()
        self._init_components()
        self._init_systems()
        self._spawn_initial_npcs()

    def _setup_spatial_index(self):
        """Construye índice espacial con tiles sólidos y colisiones de edificios."""
        self._solid_tile_index: dict[tuple[int,int], list[pygame.Rect]] = {}
        for tile in self.map_manager.solid_tiles:
            key = (tile.rect.x // TILE_SIZE, tile.rect.y // TILE_SIZE)
            self._solid_tile_index.setdefault(key, []).append(tile.rect)
        for b in self.buildings:
            for rect in b.collision_tiles:
                key = (rect.x // TILE_SIZE, rect.y // TILE_SIZE)
                self._solid_tile_index.setdefault(key, []).append(rect)

    def _init_components(self):
        """Inicializa el diccionario de componentes para el ECS."""
        self.components = {
            'Position': {}, 'Sprite': {}, 'Patrol': {}, 'MovementSpeed': {},
            'Animator': {}, 'Health': {}, 'Scale': {}, 'Identity': {},
            'Velocity': {}, 'MultiCollider': {}, 'ZLayer': {}, 'DeathTimer': {}
        }

    def _init_systems(self):
        """Configura sistemas de actualización y renderizado."""
        self.update_systems = [
            PatrolSystem(), MovementCollisionSystem(),
            DeathSystem(), AnimationSystem()
        ]
        self.render_systems = [
            HealthBarSystem(), NamePlateSystem(),
            CollisionDebugSystem(), DeathTimerDebugSystem(), DeathTimerBarSystem()
        ]

    def _spawn_initial_npcs(self):
        """Ejecuta la lógica de spawn de NPCs asegurando tiles válidos."""
        lobby_x, lobby_y = calculate_lobby_offset()
        w, h = global_map_settings.zone_size
        print(f"[ECS][Spawn] Lobby center: ({lobby_x}, {lobby_y})")
        print(f"[ECS][Spawn] Lobby size: ({w}, {h})")
        solid_coords = set(self._solid_tile_index.keys())
        tiles = [(x, y) for x in range(lobby_x, lobby_x + w) for y in range(lobby_y, lobby_y + h)]
        walkable = [t for t in tiles if t not in solid_coords]
        valid = [t for t in walkable if all(((t[0]+dx, t[1]+dy) not in solid_coords)
                                           for dx in (-1,0,1) for dy in (-1,0,1) if (dx,dy)!=(0,0))]
        bcoll = {(rect.x//TILE_SIZE, rect.y//TILE_SIZE) for b in self.buildings for rect in b.collision_tiles}
        free = [t for t in valid if t not in bcoll and all(((t[0]+dx, t[1]+dy) not in bcoll)
                                                           for dx in (-1,0,1) for dy in (-1,0,1) if (dx,dy)!=(0,0))]
        print(f"[ECS][Spawn] Spawn candidates: {len(free)}")
        for tx, ty in random.sample(free, min(1000, len(free))):
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