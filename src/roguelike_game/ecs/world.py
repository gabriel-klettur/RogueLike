from .components.position import Position
from .components.sprite import Sprite
from .components.patrol import Patrol
from .components.movement_speed import MovementSpeed
from .components.animator import Animator
from .components.scale import Scale
from .components.velocity import Velocity
from .components.collider import Collider
from .components.multi_collider import MultiCollider
from .components.mask_collider import MaskCollider
from .components.identity import Identity, Faction
from .components.health import Health
from .systems.render_system import RenderSystem
from .systems.patrol_system import PatrolSystem
from .systems.movement_collision_system import MovementCollisionSystem
from .systems.animation_system import AnimationSystem
from .systems.health_bar_system import HealthBarSystem
from .systems.nameplate_system import NamePlateSystem
from .systems.collision_debug_system import CollisionDebugSystem
from roguelike_engine.map.utils import calculate_lobby_offset
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
import pygame

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
            'MultiCollider': {}
        }
        # Systems: patrol and animation updates, then rendering
        self.update_systems = [PatrolSystem(), MovementCollisionSystem(), AnimationSystem()]
        self.render_systems = [RenderSystem(screen), HealthBarSystem(), NamePlateSystem(), CollisionDebugSystem()]

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
        # Ajustar spawn para que pies no colisionen
        cx, cy = self.find_valid_spawn(cx, cy, sprite, scale=0.25, max_radius=5, margin_tiles=1)
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
        # Velocity componente: intención de movimiento
        self.components['Velocity'][eid] = Velocity(0, 0)
        # MultiCollider con 'body' y 'feet'
        scale_comp = self.components['Scale'][eid]
        w = sprite.image.get_width()
        h = sprite.image.get_height()
        if scale_comp.scale != 1.0:
            w = int(w * scale_comp.scale)
            h = int(h * scale_comp.scale)
        # body collider basado en la máscara del sprite
        surf = sprite.image
        if scale_comp.scale != 1.0:
            surf = pygame.transform.scale(surf, (w, h))
        mask = pygame.mask.from_surface(surf)
        body = MaskCollider(mask, 0, 0)
        # feet collider (50% ancho x 20% alto)
        feet_w = int(w * 0.5)
        feet_h = int(h * 0.2)
        feet_offset_x = (w - feet_w) // 2
        feet_offset_y = h - feet_h
        feet = Collider(feet_w, feet_h, feet_offset_x, feet_offset_y)
        self.components['MultiCollider'][eid] = MultiCollider({'body': body, 'feet': feet})
        # Health component: puntos de vida actuales y máximos
        self.components['Health'][eid] = Health(current_hp=100, max_hp=100)
        # Identity component: nombre, título y facción
        self.components['Identity'][eid] = Identity(
            name=name,
            title=title,
            faction=faction
        )

    def find_valid_spawn(self, cx, cy, sprite, scale: float = 0.25, max_radius: int = 5, margin_tiles: int = 1):
        """
        Busca la celda más cercana a (cx,cy) donde el feet-collider cabe sin colisionar.
        Intenta primero con margen de tiles, si no encuentra usa margen=0.
        """
        from collections import deque
        orig_x, orig_y = cx, cy
        # dimensiones escaladas de sprite
        w = sprite.image.get_width()
        h = sprite.image.get_height()
        if scale != 1.0:
            w = int(w * scale)
            h = int(h * scale)
        feet_w = int(w * 0.5)
        feet_h = int(h * 0.2)
        offset_x = (w - feet_w) // 2
        offset_y = h - feet_h
        def bfs(margin):
            visited = {(orig_x, orig_y)}
            q = deque([(orig_x, orig_y, 0)])
            while q:
                tx, ty, dist = q.popleft()
                px = tx * TILE_SIZE - w // 2
                py = ty * TILE_SIZE - h // 2
                rect = pygame.Rect(px + offset_x, py + offset_y, feet_w, feet_h)
                if margin > 0:
                    mpx = margin * TILE_SIZE
                    rect = rect.inflate(mpx * 2, mpx * 2)
                if not any(rect.colliderect(tile.rect) for tile in self.map_manager.solid_tiles):
                    return tx, ty
                if dist < max_radius:
                    for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
                        nx, ny = tx + dx, ty + dy
                        if (nx, ny) not in visited:
                            visited.add((nx, ny))
                            q.append((nx, ny, dist + 1))
            return None
        # Intento con margen
        found = bfs(margin_tiles)
        if found:
            return found
        # Intento sin margen
        found = bfs(0)
        return found or (orig_x, orig_y)

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