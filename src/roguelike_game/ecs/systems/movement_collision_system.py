import pygame
from ..components.position import Position
from ..components.velocity import Velocity
from ..components.multi_collider import MultiCollider
from roguelike_game.ecs.utils.collider_utils import build_collider_rect

class MovementCollisionSystem:
    """
    Sistema que resuelve colisiones de movimiento usando el collider 'feet'.
    """
    def update(self, world):
        # Cache y referencias locales para rendimiento
        comps = world.components
        pos_map = comps['Position']; vel_map = comps['Velocity']; multi_map = comps['MultiCollider']
        tile_query = world.get_solid_tiles_for_rect
        buildings = getattr(world, 'buildings', [])
        # Tiles de edificio pre-flattenados
        building_tiles = [cell for b in buildings for cell in getattr(b, 'collision_tiles', [])]
        for eid in world.get_entities_with('Position', 'Velocity', 'MultiCollider'):
            pos = pos_map[eid]; vel = vel_map[eid]; multi = multi_map[eid]
            feet = multi.colliders.get('feet')
            if not feet:
                continue
            # Posicionar feet.rect usando helper
            feet.rect = build_collider_rect(pos.x, pos.y, feet)
            # Intento en X (reusa feet.rect)
            if vel.vx != 0:
                old_x = feet.rect.x; feet.rect.x += vel.vx
                blocked_map = any(feet.rect.colliderect(t) for t in tile_query(feet.rect))
                blocked_by_building = any(feet.rect.colliderect(t) for t in building_tiles)
                if not blocked_map and not blocked_by_building:
                    pos.x += vel.vx
                else:
                    feet.rect.x = old_x; vel.vx = 0
            # Intento en Y (reusa feet.rect)
            if vel.vy != 0:
                old_y = feet.rect.y; feet.rect.y += vel.vy
                blocked_map = any(feet.rect.colliderect(t) for t in tile_query(feet.rect))
                blocked_by_building = any(feet.rect.colliderect(t) for t in building_tiles)
                if not blocked_map and not blocked_by_building:
                    pos.y += vel.vy
                else:
                    feet.rect.y = old_y; vel.vy = 0
