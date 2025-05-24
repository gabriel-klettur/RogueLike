from ..components.patrol import Patrol
from ..components.position import Position
from ..components.sprite import Sprite
from ..components.movement_speed import MovementSpeed
from ..components.animator import Animator
from ..components.scale import Scale
import pygame

class PatrolSystem:
    """
    Sistema para actualizar la lógica de patrulla de NPCs.
    """
    def __init__(self):
        pass

    def update(self, world):
        # Para cada entidad con Position y Patrol
        for eid in world.get_entities_with('Position', 'Patrol'):
            pos: Position = world.components['Position'][eid]
            patrol: Patrol = world.components['Patrol'][eid]
            # Speed from component if exists
            speed_comp: MovementSpeed = world.components['MovementSpeed'].get(eid)
            speed = speed_comp.speed if speed_comp else patrol.speed
            # Asegurar índice válido
            if patrol.current_index >= len(patrol.waypoints):
                patrol.current_index = 0
            target = patrol.waypoints[patrol.current_index]
            dx = target[0] - pos.x
            dy = target[1] - pos.y
            # Si llega al waypoint, pasar al siguiente
            if abs(dx) <= speed and abs(dy) <= speed:
                pos.x, pos.y = target
                patrol.current_index = (patrol.current_index + 1) % len(patrol.waypoints)
                continue
            # Mover solo en un eje para trazar un cuadrado con colisiones
            direction = None
            # Lista de tiles sólidos para colisiones
            solid_tiles = getattr(world, 'map_manager', None).solid_tiles if hasattr(world, 'map_manager') else []
            if dx != 0:
                move = speed if dx > 0 else -speed
                sprite_comp = world.components['Sprite'][eid]
                scale_comp = world.components['Scale'].get(eid)
                w = sprite_comp.image.get_width()
                h = sprite_comp.image.get_height()
                if scale_comp and scale_comp.scale != 1.0:
                    w = int(w * scale_comp.scale)
                    h = int(h * scale_comp.scale)
                new_rect = pygame.Rect(pos.x + move, pos.y, w, h)
                if not any(new_rect.colliderect(tile.rect) for tile in solid_tiles):
                    pos.x += move
                    direction = 'right' if move > 0 else 'left'
            else:
                move = speed if dy > 0 else -speed
                sprite_comp = world.components['Sprite'][eid]
                scale_comp = world.components['Scale'].get(eid)
                w = sprite_comp.image.get_width()
                h = sprite_comp.image.get_height()
                if scale_comp and scale_comp.scale != 1.0:
                    w = int(w * scale_comp.scale)
                    h = int(h * scale_comp.scale)
                new_rect = pygame.Rect(pos.x, pos.y + move, w, h)
                if not any(new_rect.colliderect(tile.rect) for tile in solid_tiles):
                    pos.y += move
                    direction = 'down' if move > 0 else 'up'
            # Update animator state if available
            if eid in world.components['Animator']:
                animator: Animator = world.components['Animator'][eid]
                animator.current_state = direction
                continue
            # Actualizar sprite según dirección o waypoint (fallback)
            sprite_comp: Sprite = world.components['Sprite'][eid]
            if direction in patrol.sprites_by_direction and patrol.sprites_by_direction[direction]:
                sprite_comp.image = patrol.sprites_by_direction[direction][0]
            elif patrol.current_index in patrol.sprite_per_point:
                sprite_comp.image = patrol.sprite_per_point[patrol.current_index]
            elif patrol.default_sprite:
                sprite_comp.image = patrol.default_sprite
