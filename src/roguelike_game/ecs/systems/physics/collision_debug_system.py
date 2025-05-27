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
        # Cache dinámico de outlines de máscaras por imagen
        self._mask_outline_cache = {}

    def update(self, world, screen, camera):
        if not config.DEBUG:
            return
        comps = world.components
        multi_map = comps.get('MultiCollider', {})
        pos_map = comps.get('Position', {})
        sprite_map = comps.get('Sprite', {})
        cam_apply = camera.apply
        screen_rect = screen.get_rect()
        draw_polygon = pygame.draw.polygon
        draw_rect = pygame.draw.rect
        mask_cache = self._mask_outline_cache

        for eid, multi in multi_map.items():
            pos = pos_map.get(eid)
            if pos is None:
                continue
            for name, collider in multi.colliders.items():
                color = (255, 0, 0) if name == 'body' else (0, 255, 0)
                # Bounding rect culling
                rect_world = build_collider_rect(pos.x, pos.y, collider)
                screen_pos = cam_apply((rect_world.x, rect_world.y))
                self._rect.size = (rect_world.width, rect_world.height)
                self._rect.topleft = screen_pos
                if not screen_rect.colliderect(self._rect):
                    continue
                if hasattr(collider, 'mask'):
                    # Dibujar silueta escalada de la máscara según Scale
                    sprite = sprite_map.get(eid)
                    if not sprite:
                        continue
                    orig_image = sprite.image
                    scale_comp = comps.get('Scale', {}).get(eid)
                    scale_val = scale_comp.scale if scale_comp else 1.0
                    # Obtener o generar outline original
                    key_mask = id(orig_image)
                    outline = mask_cache.get(key_mask)
                    if outline is None:
                        mask_obj = pygame.mask.from_surface(orig_image)
                        outline = mask_obj.outline()
                        mask_cache[key_mask] = outline
                    if outline:
                        self._pts.clear()
                        for ox, oy in outline:
                            # Aplicar escala a offsets de la máscara
                            dx = ox * scale_val
                            dy = oy * scale_val
                            wx = pos.x + collider.offset_x * scale_val + dx
                            wy = pos.y + collider.offset_y * scale_val + dy
                            self._pts.append(cam_apply((wx, wy)))
                        draw_polygon(screen, color, self._pts, 1)
                else:
                    draw_rect(screen, color, self._rect, 1)
