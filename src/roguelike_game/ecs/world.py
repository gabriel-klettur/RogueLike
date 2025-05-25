
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
from .factories.entity_factory import spawn_monster

class NPCWorld:
    def __init__(self, screen, map_manager):
        # Referencia al mapa para colisiones
        self.map_manager = map_manager
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
        cx = lobby_x + zone_w // 2
        cy = lobby_y + zone_h // 2

        spawn_monster(self, "barbol", cx, cy)

    def create_entity(self):
        eid = len(self.entities) + 1
        self.entities.append(eid)
        return eid



    def get_entities_with(self, *component_types):
        for eid in self.entities:
            if all(eid in self.components[ctype] for ctype in component_types):
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