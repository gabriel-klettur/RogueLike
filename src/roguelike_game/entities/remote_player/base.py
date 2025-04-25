#Path: src.roguelike_project/entities/remote_player/base.py

import pygame
from src.roguelike_engine.utils.loader import load_image

class RemotePlayer:
    def __init__(self, x, y, pid, character="first_hero", direction="down", health=100, mana=50, energy=100):
        self.x = x
        self.y = y
        self.pid = pid
        self.character = character
        self.direction = direction
        self.health = health
        self.mana = mana
        self.energy = energy
        self.max_health = 100
        self.max_mana = 50
        self.max_energy = 100
        self.sprite_size = (96, 128)

        self.sprites = self.load_sprites(character)
        self.sprite = self.sprites.get(direction, list(self.sprites.values())[0])

        self.hitbox = pygame.Rect(self.x + 20, self.y + 96, 56, 28)
        self.alive = True

    def load_sprites(self, name):
        directions = [
            "up", "down", "left", "right",
            "up_left", "up_right", "down_left", "down_right"
        ]
        sprites = {}
        for dir in directions:
            path = f"assets/characters/{name}/{name}_{dir}.png"
            sprites[dir] = load_image(path, self.sprite_size)
        return sprites

    def render(self, screen, camera):
        if not self.alive:
            return
        
        if not camera.is_in_view(self.x, self.y, self.sprite_size):  # ✅ Visibilidad
            return

        scaled_sprite = pygame.transform.scale(self.sprite, camera.scale(self.sprite_size))
        pos = camera.apply((self.x, self.y))
        screen.blit(scaled_sprite, pos)

        self.render_status_bars(screen, pos, camera)
        self.render_id(screen, pos, camera)

    def render_status_bars(self, screen, pos, camera):
        x, y = pos[0] + int(18 * camera.zoom), pos[1] - int(65 * camera.zoom)
        bar_width = int(60 * camera.zoom)
        bar_height = int(20 * camera.zoom)
        spacing = int(2 * camera.zoom)
        font = pygame.font.SysFont("Arial", int(12 * camera.zoom))

        def draw_bar(current, max_value, color, y_offset):
            pygame.draw.rect(screen, (40, 40, 40), (x, y + y_offset, bar_width, bar_height))
            fill = int(bar_width * (current / max_value))
            pygame.draw.rect(screen, color, (x, y + y_offset, fill, bar_height))
            pygame.draw.rect(screen, (0, 0, 0), (x, y + y_offset, bar_width, bar_height), 1)
            text = font.render(f"{int(current)}/{int(max_value)}", True, (255, 255, 255))
            rect = text.get_rect(center=(x + bar_width // 2, y + y_offset + bar_height // 2))
            screen.blit(text, rect)

        draw_bar(self.health, self.max_health, (0, 255, 0), 0)
        draw_bar(self.mana, self.max_mana, (0, 128, 255), bar_height + spacing)
        draw_bar(self.energy, self.max_energy, (255, 50, 50), (bar_height + spacing) * 2)

    def render_id(self, screen, pos, camera):
        font = pygame.font.SysFont("Arial", int(14 * camera.zoom))
        text = font.render(self.pid[:6], True, (255, 255, 255))
        rect = text.get_rect(center=(pos[0] + int(self.sprite_size[0] * camera.zoom / 2), pos[1] - int(80 * camera.zoom)))
        screen.blit(text, rect)

    def take_damage(self, amount):
        self.health -= amount
        print(f"\U0001F4A5 Enemigo dañado: -{amount} HP")
        if self.health <= 0:
            self.alive = False
            print("\u2620\ufe0f Enemigo eliminado")

    def update(self):
        self.hitbox.topleft = (self.x + 20, self.y + 96)
