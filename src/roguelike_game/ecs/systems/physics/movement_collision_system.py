
from roguelike_game.ecs.utils.collider_utils import build_collider_rect

class MovementCollisionSystem:
    """
    Sistema que resuelve colisiones de movimiento usando el collider 'feet'.
    """
    def update(self, world):
        # Cache y referencias locales para rendimiento
        comps = world.components
        pos_map = comps['Position']; vel_map = comps['Velocity']; multi_map = comps['MultiCollider']
        # Use spatial index for all solid rects (map + buildings)
        tile_query = world.get_solid_tiles_for_rect
        for eid in world.get_entities_with('Position', 'Velocity', 'MultiCollider'):
            pos = pos_map[eid]; vel = vel_map[eid]; multi = multi_map[eid]
            feet = multi.colliders.get('feet')
            if not feet:
                continue
            # Posicionar feet.rect usando helper
            feet.rect = build_collider_rect(pos.x, pos.y, feet)
            # Intento en X (broad-phase + precise)
            if vel.vx != 0:
                old_x = feet.rect.x
                feet.rect.x += vel.vx
                # Broad-phase + precise via collidelist
                nearby = tile_query(feet.rect)
                if feet.rect.collidelist(nearby) == -1:
                    pos.x += vel.vx
                else:
                    feet.rect.x = old_x
                    vel.vx = 0
            # Intento en Y (broad-phase + precise)
            if vel.vy != 0:
                old_y = feet.rect.y
                feet.rect.y += vel.vy
                nearby = tile_query(feet.rect)
                if feet.rect.collidelist(nearby) == -1:
                    pos.y += vel.vy
                else:
                    feet.rect.y = old_y
                    vel.vy = 0
