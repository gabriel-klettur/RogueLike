# Path: src/roguelike_game/entities/player/view/player_view.py
import pygame
import roguelike_engine.config.config as config
from roguelike_engine.utils.mouse import get_direction_from_angle, draw_mouse_crosshair

class PlayerView:
    """
    Renderizado y animación del jugador, más debug de hitbox de los pies.
    """
    def __init__(self, sprites: dict[str, dict[str, list]]):
        self.state = None
        self.sprites = sprites
        self._cached_fonts = {}
        self._scaled_icons = {}

    def get_font(self, size: int) -> pygame.font.Font:
        if size not in self._cached_fonts:
            self._cached_fonts[size] = pygame.font.SysFont("Arial", size)
        return self._cached_fonts[size]

    def render(self, model, screen: pygame.Surface, camera):
        # --- Orientación hacia ratón y selección de frame ---
        mx, my = pygame.mouse.get_pos()
        world_x = mx / camera.zoom + camera.offset_x
        world_y = my / camera.zoom + camera.offset_y
        cx = model.x + model.sprite_size[0] / 2
        cy = model.y + model.sprite_size[1] / 2
        dx, dy = world_x - cx, world_y - cy
        raw_angle = pygame.math.Vector2(dx, dy).angle_to((0, -1)) % 360
        direction = get_direction_from_angle(raw_angle)
        model.direction = direction

        if model.is_walking:
            frames = self.sprites[direction]["walk"]
            idx = int(pygame.time.get_ticks() / 150) % len(frames)
            frame = frames[idx]
        else:
            frame = self.sprites[direction]["idle"][0]

        # Dibujar sprite escalado
        scaled_sprite = pygame.transform.scale(
            frame,
            camera.scale(model.sprite_size)
        )
        screen.blit(scaled_sprite, camera.apply((model.x, model.y)))

        # Actualizar rect del sprite
        model.rect = pygame.Rect(model.x, model.y, *model.sprite_size)
        # Obtener y almacenar hitbox de los pies
        foot_hitbox = model.movement.hitbox(model.x, model.y)
        model.hitbox_obj = foot_hitbox

        # DEBUG: dibujar hitbox en verde usando camera.apply y camera.scale
        if config.DEBUG:
            debug_rect = pygame.Rect(
                camera.apply((foot_hitbox.x, foot_hitbox.y)),
                camera.scale((foot_hitbox.width, foot_hitbox.height))
            )
            pygame.draw.rect(screen, (0, 255, 0), debug_rect, 1)

        # Dibujar crosshair
        draw_mouse_crosshair(screen, camera)