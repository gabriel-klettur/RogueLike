import pygame
from utils.loader import load_image
from config import DEBUG

class Player:
    def __init__(self, x, y):
        self.x = x
        self.y = y
        self.speed = 4
        self.direction = "down"  # dirección inicial
        self.sprites = self.load_sprites()
        self.sprite = self.sprites[self.direction]
        self.sprite_size = (96, 128)  # Tamaño real del sprite que estás usando
        self.hitbox = pygame.Rect(self.x + 20, self.y + 96, 56, 28)  # pies
        self.rect = pygame.Rect(self.x, self.y, *self.sprite_size)

    def load_sprites(self):
        directions = [
            "up", "down", "left", "right",
            "up_left", "up_right", "down_left", "down_right"
        ]
        sprites = {}
        for dir in directions:
            path = f"assets/characters/hero_{dir}.png"
            sprites[dir] = load_image(path, (96, 128))
        return sprites

    def move(self, dx, dy, collision_mask, obstacles):
        if dx != 0 or dy != 0:
            self.update_direction(dx, dy)
            self.sprite = self.sprites[self.direction]

        new_x = self.x + dx * self.speed
        new_y = self.y + dy * self.speed
        px = new_x + self.sprite_size[0] // 2
        py = new_y + self.sprite_size[1] - 10  # Punto de los "pies"        

        if 0 <= px < collision_mask.get_width() and 0 <= py < collision_mask.get_height():
            color = collision_mask.get_at((px, py))
            if color == pygame.Color(255, 255, 255):  # blanco = caminable
                future_hitbox = self.hitbox.move(dx * self.speed, dy * self.speed)
                for obstacle in obstacles:
                    if future_hitbox.colliderect(obstacle.rect):
                        return

                # Movimiento válido
                self.x = new_x
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

    def render(self, screen):
        screen.blit(self.sprite, (self.x, self.y))

        if DEBUG:
            pygame.draw.rect(screen, (0, 255, 0), self.hitbox, 2)
