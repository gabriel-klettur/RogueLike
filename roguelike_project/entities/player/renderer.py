import pygame
import time
from roguelike_project.config import DEBUG
from roguelike_project.utils.loader import load_image
from roguelike_project.utils.mouse import get_direction_from_angle, draw_mouse_crosshair
from roguelike_project.utils.debug import draw_player_aim_line

class PlayerRenderer:
    def __init__(self, player):
        self.player = player

    def render(self, screen, camera):
        if not camera.is_in_view(self.player.x, self.player.y, self.player.sprite_size):  # ✅ Visibilidad
            return

        # Dirección hacia el mouse
        mouse_x, mouse_y = pygame.mouse.get_pos()
        world_mouse_x = mouse_x / camera.zoom + camera.offset_x
        world_mouse_y = mouse_y / camera.zoom + camera.offset_y

        player_center_x = self.player.x + self.player.sprite_size[0] / 2
        player_center_y = self.player.y + self.player.sprite_size[1] / 2

        dx = world_mouse_x - player_center_x
        dy = world_mouse_y - player_center_y

        raw_angle = pygame.math.Vector2(dx, dy).angle_to((0, -1))
        angle = raw_angle % 360
        direction = get_direction_from_angle(angle)
        self.player.direction = direction

        # Elegir frame (idle o walk)
        if self.player.is_walking:
            frames = self.player.sprites[direction]["walk"]
            frame = int(pygame.time.get_ticks() / 150) % len(frames)
            self.player.sprite = frames[frame]
        else:
            self.player.sprite = self.player.sprites[direction]["idle"][0]

        # Dibujar sprite
        scaled_sprite = pygame.transform.scale(
            self.player.sprite,
            camera.scale(self.player.sprite_size)
        )
        screen.blit(scaled_sprite, camera.apply((self.player.x, self.player.y)))

        self.player.rect = pygame.Rect(self.player.x, self.player.y, *self.player.sprite_size)
        self.player.hitbox = pygame.Rect(self.player.x + 20, self.player.y + 96, 56, 28)

        self.draw_status_bars(screen, camera)
        draw_mouse_crosshair(screen, camera)

        if DEBUG:
            scaled_hitbox = pygame.Rect(
                camera.apply(self.player.hitbox.topleft),
                camera.scale(self.player.hitbox.size)
            )
            pygame.draw.rect(screen, (0, 255, 0), scaled_hitbox, 2)
            draw_player_aim_line(screen, camera, self.player)

        for explosion in self.player.explosions[:]:
            explosion.update()
            if explosion.finished:
                self.player.explosions.remove(explosion)

        for explosion in self.player.explosions:
            explosion.render(screen, camera)

    def draw_status_bars(self, screen, camera):
        stats = self.player.stats
        bar_width = int(60 * camera.zoom)
        bar_height = int(20 * camera.zoom)
        spacing = int(2 * camera.zoom)
        x, y = camera.apply((self.player.x + 18, self.player.y - 65))
        font = pygame.font.SysFont("Arial", int(12 * camera.zoom))

        def draw_bar(current, max_value, color, y_offset):
            pygame.draw.rect(screen, (40, 40, 40), (x, y + y_offset, bar_width, bar_height))
            fill = int(bar_width * (current / max_value))
            pygame.draw.rect(screen, color, (x, y + y_offset, fill, bar_height))
            pygame.draw.rect(screen, (0, 0, 0), (x, y + y_offset, bar_width, bar_height), 1)
            text = font.render(f"{int(current)}/{int(max_value)}", True, (255, 255, 255))
            text_rect = text.get_rect(center=(x + bar_width // 2, y + y_offset + bar_height // 2))
            screen.blit(text, text_rect)

        draw_bar(stats.health, stats.max_health, (0, 255, 0), 0)
        draw_bar(stats.mana, stats.max_mana, (0, 128, 255), bar_height + spacing)
        draw_bar(stats.energy, stats.max_energy, (255, 50, 50), (bar_height + spacing) * 2)

    def render_hud(self, screen, camera):
        icon_size = camera.scale((48, 48))
        icon = pygame.transform.scale(
            load_image("assets/ui/restore_icon.png", (48, 48)),
            icon_size
        )
        icon_x, icon_y = camera.apply((
            self.player.x + self.player.sprite_size[0] // 2 - 24,
            self.player.y + self.player.sprite_size[1] + 50
        ))
        screen.blit(icon, (icon_x, icon_y))

        now = time.time()
        elapsed = now - self.player.stats.last_restore_time
        cooldown = self.player.stats.restore_cooldown

        if elapsed < cooldown:
            ratio = 1 - (elapsed / cooldown)
            overlay_height = int(icon_size[1] * ratio)

            if overlay_height > 0:
                overlay = pygame.Surface((icon_size[0], overlay_height))
                overlay.set_alpha(180)
                overlay.fill((0, 0, 0))
                screen.blit(overlay, (icon_x, icon_y + (icon_size[1] - overlay_height)))

            font = pygame.font.SysFont("Arial", int(16 * camera.zoom))
            remaining = int(cooldown - elapsed) + 1
            text = font.render(str(remaining), True, (255, 255, 255))
            text_rect = text.get_rect(center=(icon_x + icon_size[0] // 2, icon_y + icon_size[1] // 2))
            screen.blit(text, text_rect)
