import pygame
from roguelike_project.config import DEBUG
from roguelike_project.utils.loader import load_image
import time

class PlayerRenderer:
    def __init__(self, player):
        self.player = player

    def render(self, screen, camera):
        screen.blit(self.player.sprite, camera.apply((self.player.x, self.player.y)))

        self.player.rect = pygame.Rect(self.player.x, self.player.y, *self.player.sprite_size)
        self.player.hitbox = pygame.Rect(self.player.x + 20, self.player.y + 96, 56, 28)

        self.draw_status_bars(screen, camera)

        if DEBUG:
            pygame.draw.rect(
                screen,
                (0, 255, 0),
                camera.apply(self.player.hitbox.topleft) + self.player.hitbox.size,
                2
            )

    def draw_status_bars(self, screen, camera):
        stats = self.player.stats
        bar_width = 60
        bar_height = 20
        spacing = 2
        x, y = camera.apply((self.player.x + 18, self.player.y - 65))
        font = pygame.font.SysFont("Arial", 12)

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
        icon = load_image("assets/ui/restore_icon.png", (48, 48))
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
            overlay_height = int(48 * ratio)

            if overlay_height > 0:
                overlay = pygame.Surface((48, overlay_height))
                overlay.set_alpha(180)
                overlay.fill((0, 0, 0))
                screen.blit(overlay, (icon_x, icon_y + (48 - overlay_height)))

            font = pygame.font.SysFont("Arial", 16)
            remaining = int(cooldown - elapsed) + 1
            text = font.render(str(remaining), True, (255, 255, 255))
            text_rect = text.get_rect(center=(icon_x + 24, icon_y + 24))
            screen.blit(text, text_rect)
