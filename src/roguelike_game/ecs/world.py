from .components.position import Position
from .components.sprite import Sprite
from .components.patrol import Patrol
from .components.movement_speed import MovementSpeed
from .components.animator import Animator
from .components.scale import Scale
from .components.identity import Identity, Faction
from .components.health import Health
from .systems.render_system import RenderSystem
from .systems.patrol_system import PatrolSystem
from .systems.animation_system import AnimationSystem
from .systems.health_bar_system import HealthBarSystem
from .systems.nameplate_system import NamePlateSystem
from roguelike_engine.map.utils import calculate_lobby_offset
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

class NPCWorld:
    def __init__(self, screen):
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
            'Identity': {}
        }
        # Systems: patrol and animation updates, then rendering
        self.update_systems = [PatrolSystem(), AnimationSystem()]
        self.render_systems = [RenderSystem(screen), HealthBarSystem(), NamePlateSystem()]

        # Calculate lobby center
        lobby_x, lobby_y = calculate_lobby_offset()
        zone_w, zone_h = global_map_settings.zone_size
        cx = lobby_x + zone_w // 2
        cy = lobby_y + zone_h // 2

        # Spawn one NPC at center with full identity and setup patrol
        self.spawn_npc(
            cx, cy,
            name="Barbol con tetas",
            title="Mas lista pero menos fuerte que un Barbol",
            faction=Faction.EVIL
        )

    def create_entity(self):
        eid = len(self.entities) + 1
        self.entities.append(eid)
        return eid

    def spawn_npc(
        self,
        cx,
        cy,
        name: str = "",
        title: str = "",
        faction: Faction = Faction.EVIL
    ):
        print("[ECS]: Spawning NPC at tile", cx, cy)
        eid = self.create_entity()
        # Instantiate sprite and center on tile
        sprite = Sprite("assets/npc/monsters/barbol/barbol_1_down.png")
        # Calculate pixel position centered on tile center
        px = cx * TILE_SIZE - sprite.image.get_width() // 2
        py = cy * TILE_SIZE - sprite.image.get_height() // 2
        # Assign components
        self.components['Position'][eid] = Position(px, py)
        self.components['Sprite'][eid] = sprite
        # Load directional sprites
        down_surf = sprite.image
        left_surf = Sprite("assets/npc/monsters/barbol/barbol_1_left.png").image
        right_surf = Sprite("assets/npc/monsters/barbol/barbol_1_right.png").image
        up_surf = Sprite("assets/npc/monsters/barbol/barbol_1_top.png").image
        sprites_by_direction = {
            'down': [down_surf],
            'left': [left_surf],
            'right': [right_surf],
            'up': [up_surf],
        }
        # Create patrol component with directional sprites
        patrol_comp = Patrol((px, py), sprites_by_direction=sprites_by_direction)
        patrol_comp.default_sprite = down_surf
        self.components['Patrol'][eid] = patrol_comp
        # Movement speed component (pixels per update)
        self.components['MovementSpeed'][eid] = MovementSpeed(speed=patrol_comp.speed)
        # Animator component: maps states to frames
        animator = Animator(animations=sprites_by_direction, current_state='down')
        self.components['Animator'][eid] = animator
        # Scale component: factor de escalado para el sprite (1.0 = tamaño original)
        self.components['Scale'][eid] = Scale(scale=0.25)
        # Health component: puntos de vida actuales y máximos
        self.components['Health'][eid] = Health(current_hp=100, max_hp=100)
        # Identity component: nombre, título y facción
        self.components['Identity'][eid] = Identity(
            name=name,
            title=title,
            faction=faction
        )

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