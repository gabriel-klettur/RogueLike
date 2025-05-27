import pygame

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
import roguelike_engine.config.config as config

from .systems.ai.patrol_system import PatrolSystem
from .systems.physics.movement_collision_system import MovementCollisionSystem
from .systems.rendering.animation_system import AnimationSystem
from .systems.rendering.health_bar_system import HealthBarSystem
from .systems.rendering.nameplate_system import NamePlateSystem
from .systems.physics.collision_debug_system import CollisionDebugSystem
from .systems.combat.death_system import DeathSystem
from .systems.combat.death_timer_debug_system import DeathTimerDebugSystem
from .systems.rendering.death_timer_bar_system import DeathTimerBarSystem
from .systems.ai.npc_melee_decision_system import NPCMeleeDecisionSystem
from .systems.combat.melee_combat_system import MeleeCombatSystem
from .systems.combat.attack_cooldown_system import AttackCooldownSystem
from .systems.ai.aggro_system import AggroSystem
from .systems.ai.chase_system import ChaseSystem
from .systems.physics.facing_system import FacingSystem
from .systems.core.spawn_debug_system import SpawnDebugSystem
from .systems.core.spawn_system import SpawnSystem
from .systems.input.input_system import InputSystem

from roguelike_engine.map.utils import calculate_lobby_offset
from .utils.spawn_utils import find_spawn_positions
from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest

class NPCWorld:
    def __init__(self, screen, map_manager, buildings):
        """Inicializa NPCWorld: configura índice espacial, componentes, sistemas y spawn inicial."""
        self.screen = screen
        self.map_manager = map_manager
        self.buildings = buildings
        self.entities = []
        self.next_entity_id = 1     # next_entity_id para garantizar IDs únicos

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
            'Velocity': {}, 'MultiCollider': {}, 'ZLayer': {}, 'DeathTimer': {},
            'SpawnRequest': {},
            'CombatStats': {}, 'MeleeWeapon': {},
            'WantsToMelee': {}, 'AttackCooldown': {},
            'AggroRange': {}, 'ChaseTarget': {}, 'FacingCooldown': {}, 'InputComponent': {}, 'InventoryComponent': {}, 'CameraFollowComponent': {}, 'PlayerTagComponent': {}
        }

    def _init_systems(self):
        """Configura sistemas de actualización y renderizado."""
        self.update_systems = [
            AggroSystem(), ChaseSystem(), FacingSystem(), InputSystem(),
            PatrolSystem(), MovementCollisionSystem(),
            AttackCooldownSystem(),
            NPCMeleeDecisionSystem(),
            MeleeCombatSystem(),
            DeathSystem(), AnimationSystem(), SpawnSystem()
        ]
        self.render_systems = [
            HealthBarSystem(), NamePlateSystem(),
            CollisionDebugSystem(), DeathTimerDebugSystem(), DeathTimerBarSystem()
        ]
        # Añadir sistema de debug de spawn cuando DEBUG=true
        if config.DEBUG:
            self.render_systems.append(SpawnDebugSystem())

    def _spawn_initial_npcs(self):
        """Ejecuta la lógica de spawn de NPCs asegurando tiles válidos."""

        #!-------------------------------- LOBBY ---------------------------------------------------
        # Delegar selección de posiciones de spawn a spawn_utils
        lobby_offset = calculate_lobby_offset()
        zone_size = global_map_settings.zone_size

        #! El neighbor_padding deberia calcularse de forma automatica teniendo encuenta el numero de tiles que utiliza el collider de los pies del npc
        positions = find_spawn_positions(
            self.map_manager, self.buildings,
            lobby_offset, zone_size,
            neighbor_padding=3, sample_count=500
        )
        print(f"[ECS][Spawn] Spawn candidates: {len(positions)}")
        for tx, ty in positions:
            # Crear request de spawn en ECS
            eid_req = self.create_entity()
            self.components['SpawnRequest'][eid_req] = SpawnRequest(
                prototype="barbol", position=(tx, ty)
            )
        #!-------------------------------- EMPTY ZONE LEFT--------------------------------------------------
        # Spawn 20 NPCs 'barbol' in the empty_left zone
        offsets = global_map_settings.zone_offsets
        empty_offset = offsets.get('empty_left')
        if empty_offset:
            empty_positions = find_spawn_positions(
                self.map_manager, self.buildings,
                empty_offset, zone_size,
                neighbor_padding=3, sample_count=500
            )
            print(f"[ECS][Spawn] Spawn in empty_left: {len(empty_positions)}")
            for tx, ty in empty_positions:
                eid_req = self.create_entity()
                self.components['SpawnRequest'][eid_req] = SpawnRequest(
                    prototype="barbol", position=(tx, ty)
                )
        #!-----------------------------------------------------------------------------------

    def create_entity(self):
        # Asignar ID secuencial único
        eid = self.next_entity_id
        self.next_entity_id += 1
        self.entities.append(eid)
        return eid

    def get_entities_with(self, *component_types):
        if not component_types:
            return
        comps = self.components
        # Elegir el componente con menos entidades para iterar
        dicts = [comps.get(ct, {}) for ct in component_types]
        smallest = min(dicts, key=lambda d: len(d))
        for eid in smallest:
            # comprobar que eid exista en todos los componentes solicitados
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