import pygame
from utils.loader import load_image

class Player:
    def __init__(self, x, y):
        self.x = x
        self.y = y
        self.speed = 4
        self.direction = "down"  # dirección inicial
        self.sprites = self.load_sprites()
        self.sprite = self.sprites[self.direction]

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

    def move(self, dx, dy, collision_mask):
        if dx == 0 and dy == 0:
            return  # no se está moviendo, evitar cambio de sprite innecesario

        self.update_direction(dx, dy)  # <--- ACTUALIZA LA DIRECCIÓN
        self.sprite = self.sprites[self.direction]  # <--- ACTUALIZA EL SPRITE

        new_x = self.x + dx * self.speed
        new_y = self.y + dy * self.speed

        # Punto central del sprite
        px = new_x + 16
        py = new_y + 16

        # Validar que no se sale del mapa
        if 0 <= px < collision_mask.get_width() and 0 <= py < collision_mask.get_height():
            color = collision_mask.get_at((px, py))

            if color == pygame.Color(255, 255, 255):  # blanco = caminable
                self.x = new_x
                self.y = new_y

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
