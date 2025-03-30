import pygame
from utils.loader import load_image
from config import DEBUG

class Player:
    def __init__(self, x, y, character_name="first_hero"):
        self.character_name = character_name
        self.x = x
        self.y = y
        self.speed = 4
        self.direction = "down"
        self.sprite_size = (96, 128)

        # 🎯 Stats personalizados según el personaje
        if character_name == "valkyria":
            self.max_health = 120
            self.max_mana = 80
            self.max_energy = 60
        else:
            self.max_health = 100
            self.max_mana = 50
            self.max_energy = 100

        self.health = self.max_health
        self.mana = self.max_mana
        self.energy = self.max_energy

        self.load_character_assets()

    def load_character_assets(self):
        self.sprites = self.load_sprites(self.character_name)
        self.sprite = self.sprites[self.direction]
        self.hitbox = pygame.Rect(self.x + 20, self.y + 96, 56, 28)
        self.rect = pygame.Rect(self.x, self.y, *self.sprite_size)

    def load_sprites(self, character_name):
        directions = [
            "up", "down", "left", "right",
            "up_left", "up_right", "down_left", "down_right"
        ]
        sprites = {}
        for dir in directions:
            path = f"assets/characters/{character_name}/{character_name}_{dir}.png"
            sprites[dir] = load_image(path, self.sprite_size)
        return sprites

    def change_character(self, new_character_name):
        self.character_name = new_character_name
        self.__init__(self.x, self.y, new_character_name)  # reinicia stats y sprites

    def move(self, dx, dy, collision_mask, obstacles):
        if dx != 0 or dy != 0:
            self.update_direction(dx, dy)
            self.sprite = self.sprites[self.direction]

        if dx != 0:
            new_x = self.x + dx * self.speed
            px = new_x + self.sprite_size[0] // 2
            py = self.y + self.sprite_size[1] - 10
            if 0 <= px < collision_mask.get_width() and 0 <= py < collision_mask.get_height():
                color = collision_mask.get_at((px, py))
                if color == pygame.Color(255, 255, 255):
                    future_hitbox = self.hitbox.move(dx * self.speed, 0)
                    if not any(future_hitbox.colliderect(ob.rect) for ob in obstacles):
                        self.x = new_x
                        self.hitbox.topleft = (self.x + 20, self.y + 96)

        if dy != 0:
            new_y = self.y + dy * self.speed
            px = self.x + self.sprite_size[0] // 2
            py = new_y + self.sprite_size[1] - 10
            if 0 <= px < collision_mask.get_width() and 0 <= py < collision_mask.get_height():
                color = collision_mask.get_at((px, py))
                if color == pygame.Color(255, 255, 255):
                    future_hitbox = self.hitbox.move(0, dy * self.speed)
                    if not any(future_hitbox.colliderect(ob.rect) for ob in obstacles):
                        self.y = new_y
                        self.hitbox.topleft = (self.x + 20, self.y + 96)

    def update_direction(self, dx, dy):
        if dx == -1 and dy == -1:
            self.direction = "up_left"
        elif dx == 1 and dy == -1:
            self.direction = "up_right"
        elif dx == -1 and dy == 1:
            self.direction = "down_left"
        elif dx == 1 and dy == 1:
            self.direction = "down_right"
        elif dx == -1:
            self.direction = "left"
        elif dx == 1:
            self.direction = "right"
        elif dy == -1:
            self.direction = "up"
        elif dy == 1:
            self.direction = "down"

    def render(self, screen, camera):
        # Dibuja el sprite
        screen.blit(self.sprite, camera.apply((self.x, self.y)))

        # Dibuja barras de estado
        self.draw_status_bars(screen, camera)

        if DEBUG:
            pygame.draw.rect(screen, (0, 255, 0), camera.apply(self.hitbox.topleft) + self.hitbox.size, 2)

    def draw_status_bars(self, screen, camera):
        bar_width = 60
        bar_height = 20
        spacing = 2
        x, y = camera.apply((self.x + 18, self.y - 65))
        font = pygame.font.SysFont("Arial", 12)

        def draw_bar(current, max_value, color, y_offset):
            # FONDO
            pygame.draw.rect(screen, (40, 40, 40), (x, y + y_offset, bar_width, bar_height))

            # VALOR LLENO
            fill = int(bar_width * (current / max_value))
            pygame.draw.rect(screen, color, (x, y + y_offset, fill, bar_height))

            # BORDE
            pygame.draw.rect(screen, (0, 0, 0), (x, y + y_offset, bar_width, bar_height), 1)

            # TEXTO CENTRADO
            text = font.render(f"{int(current)}/{int(max_value)}", True, (255, 255, 255))
            text_rect = text.get_rect(center=(x + bar_width // 2, y + y_offset + bar_height // 2))
            screen.blit(text, text_rect)

        draw_bar(self.health, self.max_health, (0, 255, 0), 0)
        draw_bar(self.mana, self.max_mana, (0, 128, 255), bar_height + spacing)
        draw_bar(self.energy, self.max_energy, (255, 50, 50), (bar_height + spacing) * 2)
