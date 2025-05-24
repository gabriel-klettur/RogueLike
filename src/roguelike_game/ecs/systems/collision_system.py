from ..components.position import Position
from ..components.velocity import Velocity
from ..components.collider import Collider

class CollisionSystem:
    """
    Sistema que resuelve colisiones para entidades con Position, Velocity y Collider.
    """
    def update(self, world):
        for eid in world.get_entities_with('Position', 'Velocity', 'Collider'):
            pos = world.components['Position'][eid]
            vel = world.components['Velocity'][eid]
            col = world.components['Collider'][eid]
            # Sincronizar collider con posición actual
            col.rect.topleft = (pos.x + col.offset_x, pos.y + col.offset_y)
            # Resolver movimiento eje X
            if vel.vx != 0:
                new_rect = col.rect.move(vel.vx, 0)
                if not any(new_rect.colliderect(tile.rect) for tile in world.map_manager.solid_tiles):
                    pos.x += vel.vx
                    col.rect = new_rect
                else:
                    vel.vx = 0
            # Resolver movimiento eje Y
            if vel.vy != 0:
                new_rect = col.rect.move(0, vel.vy)
                if not any(new_rect.colliderect(tile.rect) for tile in world.map_manager.solid_tiles):
                    pos.y += vel.vy
                    col.rect = new_rect
                else:
                    vel.vy = 0
