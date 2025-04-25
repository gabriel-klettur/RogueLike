import pygame
import time
import src.roguelike_project.config as config
from src.roguelike_engine.utils.loader import load_image
from src.roguelike_engine.utils.mouse import get_direction_from_angle, draw_mouse_crosshair
from src.roguelike_engine.utils.debug import draw_player_aim_line


class PlayerRenderer:
    def __init__(self, player):
        self.state = None
        self.player = player
        self._cached_fonts = {}
        self._scaled_icons = {}

        restore_icon = load_image("assets/ui/restore_icon.png", (48, 48))
        dash_icon = load_image("assets/ui/dash_icon.png", (48, 48))
        slash_icon = load_image("assets/ui/slash_icon.png", (48, 48))
        shield_icon = load_image("assets/ui/shield_icon.png", (48, 48))
        firework_icon = load_image("assets/ui/firework_icon.png", (48, 48))
        smoke_icon = load_image("assets/ui/smoke_icon.png", (48, 48))
        lightning_icon = load_image("assets/ui/lightning_icon.png", (48, 48))
        pixel_fire_icon = load_image("assets/ui/pixel_fire_icon.png", (48, 48))
        teleport_icon = load_image("assets/ui/teleport_icon.png", (48, 48))
        generic_icon = load_image("assets/ui/generic_icon.png", (48, 48))

        self.abilities = [
            {
                "name": "Restore",
                "key": "Q",
                "cooldown": lambda: self.player.stats.restore_cooldown,
                "last_used": lambda: self.player.stats.last_restore_time,
                "icon": restore_icon
            },
            {
                "name": "Dash",
                "key": "V",
                "cooldown": lambda: 2,
                "last_used": lambda: self.player.movement.last_dash_time,
                "icon": dash_icon
            },
            {
                "name": "Slash",
                "key": "E",
                "cooldown": lambda: 0.5,
                "last_used": lambda: self.player.attack.last_attack_time,
                "icon": slash_icon
            },
            {
                "name": "Shield",
                "key": "1",
                "cooldown": lambda: 10,
                "last_used": lambda: self.player.stats.last_shield_time,
                "icon": shield_icon
            },
            {
                "name": "Firework",
                "key": "F",
                "cooldown": lambda: self.player.stats.firework_cooldown,
                "last_used": lambda: self.player.stats.last_firework_time,
                "icon": firework_icon
            },
            {
                "name": "Smoke",
                "key": "R",
                "cooldown": lambda: self.player.stats.smoke_cooldown,
                "last_used": lambda: self.player.stats.last_smoke_time,
                "icon": smoke_icon
            },
            {
                "name": "Lightning",
                "key": "Z",
                "cooldown": lambda: self.player.stats.lightning_cooldown,
                "last_used": lambda: self.player.stats.last_lightning_time,
                "icon": lightning_icon
            },
            {
                "name": "Pixel Fire",
                "key": "X",
                "cooldown": lambda: self.player.stats.pixel_fire_cooldown,
                "last_used": lambda: self.player.stats.last_pixel_fire_time,
                "icon": pixel_fire_icon
            },
            {
                "name": "Teleport",
                "key": "C",
                "cooldown": lambda: self.player.movement.teleport_cooldown,
                "last_used": lambda: self.player.movement.last_teleport_time,
                "icon": teleport_icon
            }
        ]

    def get_font(self, size):
        if size not in self._cached_fonts:
            self._cached_fonts[size] = pygame.font.SysFont("Arial", size)
        return self._cached_fonts[size]

    def render(self, screen, camera):
        if not camera.is_in_view(self.player.x, self.player.y, self.player.sprite_size):
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

        # Elegir frame
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
        self.render_hud(screen, camera)
        draw_mouse_crosshair(screen, camera)

        if config.DEBUG:
            scaled_hitbox = pygame.Rect(
                camera.apply(self.player.hitbox.topleft),
                camera.scale(self.player.hitbox.size)
            )
            pygame.draw.rect(screen, (0, 255, 0), scaled_hitbox, 2)
            draw_player_aim_line(screen, camera, self.player)

    def draw_status_bars(self, screen, camera):
        stats = self.player.stats
        bar_width = int(60 * camera.zoom)
        bar_height = int(20 * camera.zoom)
        spacing = int(2 * camera.zoom)
        x, y = camera.apply((self.player.x + 18, self.player.y - 65))
        font = self.get_font(int(12 * camera.zoom))

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
        now = time.time()
        font = self.get_font(16)
        icon_size = 48
        spacing = 60

        total_width = len(self.abilities) * spacing
        start_x = (screen.get_width() - total_width) // 2
        start_y = screen.get_height() - 90

        for i, ability in enumerate(self.abilities):
            if icon_size not in self._scaled_icons:
                self._scaled_icons[icon_size] = {}
            if ability["name"] not in self._scaled_icons[icon_size]:
                self._scaled_icons[icon_size][ability["name"]] = pygame.transform.scale(ability["icon"], (icon_size, icon_size))

            icon = self._scaled_icons[icon_size][ability["name"]]
            icon_x = start_x + i * spacing
            icon_y = start_y

            # Dibujar ícono
            screen.blit(icon, (icon_x, icon_y))

            # Cooldown visual
            elapsed = now - ability["last_used"]()
            cooldown = ability["cooldown"]()
            if elapsed < cooldown:
                ratio = 1 - (elapsed / cooldown)
                overlay_height = int(icon_size * ratio)
                if overlay_height > 0:
                    overlay = pygame.Surface((icon_size, overlay_height), pygame.SRCALPHA)
                    overlay.fill((0, 0, 0, 180))
                    screen.blit(overlay, (icon_x, icon_y + (icon_size - overlay_height)))

                remaining = int(cooldown - elapsed) + 1
                text = font.render(str(remaining), True, (255, 255, 255))
                text_rect = text.get_rect(center=(icon_x + icon_size // 2, icon_y + icon_size // 2))
                screen.blit(text, text_rect)

            # Tecla asignada
            key_font = self.get_font(14)
            key_text = key_font.render(ability["key"], True, (255, 255, 0))
            key_rect = key_text.get_rect(center=(icon_x + icon_size // 2, icon_y - 12))
            screen.blit(key_text, key_rect)
