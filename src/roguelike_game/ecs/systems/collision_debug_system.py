import pygame
import roguelike_engine.config.config as config

class CollisionDebugSystem:
    """
    Dibuja las cajas de colisión de entidades cuando DEBUG=True.
    """
    def update(self, world, screen, camera):
        if not config.DEBUG:
            return
        for eid, multi in world.components.get('MultiCollider', {}).items():
            pos = world.components['Position'][eid]
            for name, collider in multi.colliders.items():
                # Color según sub-collider
                color = (255, 0, 0) if name == 'body' else (0, 255, 0)
                if hasattr(collider, 'mask'):
                    # Dibujar silueta de la máscara
                    outline = collider.mask.outline()
                    if outline:
                        pts = []
                        for ox, oy in outline:
                            wx = pos.x + collider.offset_x + ox
                            wy = pos.y + collider.offset_y + oy
                            pts.append(camera.apply((wx, wy)))
                        pygame.draw.polygon(screen, color, pts, 1)
                else:
                    # Dibujar rectángulo para collider rectangular
                    rect_world = pygame.Rect(
                        pos.x + collider.offset_x,
                        pos.y + collider.offset_y,
                        collider.width,
                        collider.height
                    )
                    screen_pos = camera.apply((rect_world.x, rect_world.y))
                    rect_screen = pygame.Rect(screen_pos, (rect_world.width, rect_world.height))
                    pygame.draw.rect(screen, color, rect_screen, 1)
