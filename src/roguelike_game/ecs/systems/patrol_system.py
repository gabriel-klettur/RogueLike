from ..components.patrol import Patrol
from ..components.position import Position
from ..components.sprite import Sprite
from ..components.movement_speed import MovementSpeed
from ..components.animator import Animator
from ..components.scale import Scale
from ..components.velocity import Velocity
from ..components.multi_collider import MultiCollider
import pygame

class PatrolSystem:
    """
    Sistema para actualizar la lógica de patrulla de NPCs.
    """
    def __init__(self):
        pass

    def update(self, world):
        # Requerimos Position, Patrol, Velocity y MultiCollider para colisión en patrulla
        for eid in world.get_entities_with('Position', 'Patrol', 'Velocity', 'MultiCollider'):
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
            # Resetear velocidad y test de colisión para salto de waypoint
            direction = None
            vel_comp: Velocity = world.components['Velocity'][eid]
            vel_comp.vx = vel_comp.vy = 0
            multi: MultiCollider = world.components['MultiCollider'][eid]
            feet = multi.colliders.get('feet')
            if not feet:
                continue
            feet_rect = pygame.Rect(
                pos.x + feet.offset_x,
                pos.y + feet.offset_y,
                feet.width,
                feet.height
            )
            moved = False
            # Intentar mover en X primero
            if dx != 0:
                step_x = speed if dx > 0 else -speed
                new_x = feet_rect.move(step_x, 0)
                if not any(new_x.colliderect(t.rect) for t in getattr(world.map_manager, 'solid_tiles', [])):
                    vel_comp.vx = step_x
                    direction = 'right' if step_x > 0 else 'left'
                    moved = True
                else:
                    # Fallback en Y
                    if dy != 0:
                        step_y = speed if dy > 0 else -speed
                        new_y = feet_rect.move(0, step_y)
                        if not any(new_y.colliderect(t.rect) for t in getattr(world.map_manager, 'solid_tiles', [])):
                            vel_comp.vy = step_y
                            direction = 'down' if step_y > 0 else 'up'
                            moved = True
            else:
                # Intentar mover en Y
                step_y = speed if dy > 0 else -speed
                new_y = feet_rect.move(0, step_y)
                if not any(new_y.colliderect(t.rect) for t in getattr(world.map_manager, 'solid_tiles', [])):
                    vel_comp.vy = step_y
                    direction = 'down' if step_y > 0 else 'up'
                    moved = True
                else:
                    # Fallback en X
                    if dx != 0:
                        step_x = speed if dx > 0 else -speed
                        new_x = feet_rect.move(step_x, 0)
                        if not any(new_x.colliderect(t.rect) for t in getattr(world.map_manager, 'solid_tiles', [])):
                            vel_comp.vx = step_x
                            direction = 'right' if step_x > 0 else 'left'
                            moved = True
            # Si sigue bloqueado, saltar waypoint
            if not moved:
                patrol.current_index = (patrol.current_index + 1) % len(patrol.waypoints)
                continue
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
