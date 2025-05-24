import pygame
import roguelike_engine.config.config as config

class CollisionDebugSystem:
    """
    Dibuja las cajas de colisión de entidades cuando DEBUG=True.
    """
    def update(self, world, screen, camera):
        if not config.DEBUG:
            return
        for eid, collider in world.components.get('Collider', {}).items():
            pos = world.components['Position'][eid]
            # Rect en coordenadas de mundo
            rect_world = pygame.Rect(
                pos.x + collider.offset_x,
                pos.y + collider.offset_y,
                collider.width,
                collider.height
            )
            # Transformar a coordenadas de pantalla
            screen_pos = camera.apply((rect_world.x, rect_world.y))
            rect_screen = pygame.Rect(screen_pos, (rect_world.width, rect_world.height))
            # Dibujar rectángulo en magenta
            pygame.draw.rect(screen, (255, 0, 255), rect_screen, 1)
