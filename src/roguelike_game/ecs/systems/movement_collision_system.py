import pygame
from ..components.position import Position
from ..components.velocity import Velocity
from ..components.multi_collider import MultiCollider

class MovementCollisionSystem:
    """
    Sistema que resuelve colisiones de movimiento usando el collider 'feet'.
    """
    def update(self, world):
        for eid in world.get_entities_with('Position', 'Velocity', 'MultiCollider'):
            pos = world.components['Position'][eid]
            vel = world.components['Velocity'][eid]
            multi = world.components['MultiCollider'][eid]
            feet = multi.colliders.get('feet')
            if not feet:
                continue
            # Sincronizar rect feet con la posición actual
            feet.rect.topleft = (pos.x + feet.offset_x, pos.y + feet.offset_y)
            # Mover en X
            if vel.vx != 0:
                new_rect = feet.rect.move(vel.vx, 0)
                if not any(new_rect.colliderect(tile.rect) for tile in world.map_manager.solid_tiles):
                    pos.x += vel.vx
                    feet.rect = new_rect
                else:
                    vel.vx = 0
            # Mover en Y
            if vel.vy != 0:
                new_rect = feet.rect.move(0, vel.vy)
                if not any(new_rect.colliderect(tile.rect) for tile in world.map_manager.solid_tiles):
                    pos.y += vel.vy
                    feet.rect = new_rect
                else:
                    vel.vy = 0
