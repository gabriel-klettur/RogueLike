"""
Renderizado y animación del jugador.
"""
import pygame
from roguelike_engine.utils.mouse import get_direction_from_angle

class PlayerView:
    def __init__(self, sprites: dict[str,dict[str,list]]):
        self.state = None
        self.sprites = sprites
        self._last_frame_time = 0

    def render(self, model, screen, camera):
        # Determinar orientación hacia ratón
        mx, my = pygame.mouse.get_pos()
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        cx, cy = model.center()
        dx, dy = wx - cx, wy - cy
        angle = pygame.math.Vector2(dx, dy).angle_to((0, -1)) % 360
        direction = get_direction_from_angle(angle)
        model.direction = direction

        # Seleccionar frame
        if model.is_walking:
            frames = self.sprites[direction]['walk']
            idx = int(pygame.time.get_ticks()/150) % len(frames)
            frame = frames[idx]
        else:
            frame = self.sprites[direction]['idle'][0]

        # Dibujar
        scaled = pygame.transform.scale(frame, camera.scale(model.sprite_size))
        screen.blit(scaled, camera.apply((model.x, model.y)))

        # Actualizar rect y hitbox
        model.rect = pygame.Rect(model.x, model.y, *model.sprite_size)
        model.hitbox_obj = model.hitbox()