import pygame
import roguelike_engine.config.config as config
from roguelike_game.ecs.utils.collider_utils import build_collider_rect

class CollisionDebugSystem:
    """
    Dibuja las cajas de colisión de entidades cuando DEBUG=True.
    """
    def __init__(self):
        # Reusable rect for bounding culling
        self._rect = pygame.Rect(0, 0, 0, 0)
        # Reusable list for mask points
        self._pts = []

    def update(self, world, screen, camera):
        if not config.DEBUG:
            return
        comps = world.components
        multi_map = comps.get('MultiCollider', {})
        pos_map = comps.get('Position', {})
        cam_apply = camera.apply
        screen_rect = screen.get_rect()
        draw_polygon = pygame.draw.polygon
        draw_rect = pygame.draw.rect
        for eid, multi in multi_map.items():
            pos = pos_map.get(eid)
            if pos is None:
                continue
            for name, collider in multi.colliders.items():
                color = (255, 0, 0) if name == 'body' else (0, 255, 0)
                # Bounding rect culling using reusable rect
                rect_world = build_collider_rect(pos.x, pos.y, collider)
                screen_pos = cam_apply((rect_world.x, rect_world.y))
                self._rect.size = (rect_world.width, rect_world.height)
                self._rect.topleft = screen_pos
                if not screen_rect.colliderect(self._rect):
                    continue
                if hasattr(collider, 'mask'):
                    # Cache mask outline
                    if not hasattr(collider, '_outline_cache'):
                        collider._outline_cache = collider.mask.outline()
                    outline = collider._outline_cache
                    if outline:
                        # Reuse pts list
                        self._pts.clear()
                        for ox, oy in outline:
                            self._pts.append(
                                cam_apply((pos.x + collider.offset_x + ox,
                                           pos.y + collider.offset_y + oy))
                            )
                        draw_polygon(screen, color, self._pts, 1)
                else:
                    draw_rect(screen, color, self._rect, 1)
