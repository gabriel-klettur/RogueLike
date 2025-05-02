import pygame
import math
from src.roguelike_engine.utils.loader import load_image
import src.roguelike_engine.config as config

class Monster:
    def __init__(self, x, y, name="Monster"):
        self.x = x
        self.y = y
        self.health = 60
        self.max_health = 60
        self.sprite_size = (256, 256)
        self.name = name

        # Sprites por dirección
        self.sprites = {
            "up": load_image("assets/npc/monsters/barbol/barbol_1_top.png", self.sprite_size),
            "down": load_image("assets/npc/monsters/barbol/barbol_1_down.png", self.sprite_size),
            "left": load_image("assets/npc/monsters/barbol/barbol_1_left.png", self.sprite_size),
            "right": load_image("assets/npc/monsters/barbol/barbol_1_right.png", self.sprite_size),
        }
        self.sprite = self.sprites["down"]
        self.mask = pygame.mask.from_surface(self.sprite)

        self.hitbox = pygame.Rect(self.x + 20, self.y + 96, 56, 28)
        self.alive = True

        # Patrullaje
        self.speed = 5
        self.path = [
            (0, -1, 200),
            (1, 0, 50),
            (0, 1, 200),
            (-1, 0, 50),
        ]
        self.current_step = 0
        self.step_progress = 0

    def take_damage(self, amount):
        self.health -= amount        
        if self.health <= 0:
            self.alive = False            

    def update(self, state):
        if not self.alive:
            return

        player = state.player
        distance = self.calculate_distance(player)
        
        if distance <= 500:
            self.follow_player(player)
        else:
            self.continue_patrol()

        self.mask = pygame.mask.from_surface(self.sprite)
        self.hitbox.topleft = (self.x + 20, self.y + 96)

    def calculate_distance(self, player):
        dx = player.x - self.x
        dy = player.y - self.y
        return math.sqrt(dx**2 + dy**2)

    def follow_player(self, player):
        dx = player.x - self.x
        dy = player.y - self.y
        distance = self.calculate_distance(player)
        
        if distance < 250:  # Minimum distance threshold
            # Move away from player
            self.update_sprite_direction((dx, dy))
        else:
            # Move towards player but keep distance
            direction = (dx/distance, dy/distance)
            self.x += direction[0] * self.speed
            self.y += direction[1] * self.speed
            self.update_sprite_direction(direction)

    def continue_patrol(self):
        dx, dy, distance = self.path[self.current_step]

        if dx == 1:
            self.sprite = self.sprites["right"]
        elif dx == -1:
            self.sprite = self.sprites["left"]
        elif dy == -1:
            self.sprite = self.sprites["up"]
        elif dy == 1:
            self.sprite = self.sprites["down"]

        self.x += dx * self.speed
        self.y += dy * self.speed
        self.step_progress += self.speed

        if self.step_progress >= distance:
            self.current_step = (self.current_step + 1) % len(self.path)
            self.step_progress = 0

    def update_sprite_direction(self, direction):
        if abs(direction[0]) > abs(direction[1]):
            self.sprite = self.sprites["right"] if direction[0] > 0 else self.sprites["left"]
        else:
            self.sprite = self.sprites["down"] if direction[1] > 0 else self.sprites["up"]

    def render(self, screen, camera):
        if not self.alive:
            return

        if not camera.is_in_view(self.x, self.y, self.sprite_size):
            return

        scaled_sprite = pygame.transform.scale(self.sprite, camera.scale(self.sprite_size))
        screen.blit(scaled_sprite, camera.apply((self.x, self.y)))

        self.render_health_bar(screen, camera)

        if config.DEBUG:
            outline = self.mask.outline()
            scaled_outline = [camera.apply((self.x + x, self.y + y)) for x, y in outline]
            if len(scaled_outline) >= 3:
                pygame.draw.polygon(screen, (255, 0, 0), scaled_outline, 1)

    def render_health_bar(self, screen, camera):
        x, y = camera.apply((self.x + 18, self.y - 20))

        #0.9 es un valor un poco arbitrario porque el 100% de la barra no concordaba visualmente con el cuadrado del render
        bar_width = int(self.sprite_size[0] * camera.zoom * 0.9)
        bar_height = int(20 * camera.zoom)

        #Dibujar barra de salud
        pygame.draw.rect(screen, (40, 40, 40), (x, y, bar_width, bar_height))
        fill = int(bar_width * (self.health / self.max_health))
        pygame.draw.rect(screen, (0, 255, 0), (x, y, fill, bar_height))
        pygame.draw.rect(screen, (0, 0, 0), (x, y, bar_width, bar_height), 1)

        #Insertar valor numero de vida
        health_text = f"{self.health}/{self.max_health}"
        font = pygame.font.SysFont('Arial', int(18 * camera.zoom))  # Adjust font size with zoom
        text_surface = font.render(health_text, True, (255, 0, 0))
        # Center the text in the health bar
        text_rect = text_surface.get_rect(center=(x + bar_width // 2, y + bar_height // 2))
            # Only draw the text if it fits in the filled portion of the health bar

        if fill > text_rect.width * 0.8:  # 80% of text width as a safety margin
            screen.blit(text_surface, text_rect)
        else:
            # Draw the text to the right of the health bar if it doesn't fit inside
            text_rect.left = x + bar_width + 2  # Small offset from the bar
            screen.blit(text_surface, text_rect)
