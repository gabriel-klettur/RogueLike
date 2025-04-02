import pygame
from roguelike_project.utils.loader import load_image
from roguelike_project.config import DEBUG

class Monster:
    def __init__(self, x, y):
        self.x = x
        self.y = y
        self.health = 60
        self.max_health = 60
        self.sprite_size = (256, 256)

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
        print(f"💥 Monstruo dañado: -{amount} HP")
        if self.health <= 0:
            self.alive = False
            print("☠️ Monstruo eliminado")

    def update(self):
        if not self.alive:
            return

        dx, dy, distance = self.path[self.current_step]

        if dx == 1:
            self.sprite = self.sprites["right"]
        elif dx == -1:
            self.sprite = self.sprites["left"]
        elif dy == -1:
            self.sprite = self.sprites["up"]
        elif dy == 1:
            self.sprite = self.sprites["down"]

        # ✅ Actualizar máscara
        self.mask = pygame.mask.from_surface(self.sprite)

        self.x += dx * self.speed
        self.y += dy * self.speed
        self.step_progress += self.speed

        if self.step_progress >= distance:
            self.current_step = (self.current_step + 1) % len(self.path)
            self.step_progress = 0

        self.hitbox.topleft = (self.x + 20, self.y + 96)

    def render(self, screen, camera):
        if not self.alive:
            return

        scaled_sprite = pygame.transform.scale(self.sprite, camera.scale(self.sprite_size))
        screen.blit(scaled_sprite, camera.apply((self.x, self.y)))

        self.render_health_bar(screen, camera)

        if DEBUG:
            outline = self.mask.outline()
            scaled_outline = [camera.apply((self.x + x, self.y + y)) for x, y in outline]
            if len(scaled_outline) >= 3:
                pygame.draw.polygon(screen, (255, 0, 0), scaled_outline, 1)

    def render_health_bar(self, screen, camera):
        x, y = camera.apply((self.x + 18, self.y - 20))
        bar_width = int(60 * camera.zoom)
        bar_height = int(10 * camera.zoom)
        pygame.draw.rect(screen, (40, 40, 40), (x, y, bar_width, bar_height))
        fill = int(bar_width * (self.health / self.max_health))
        pygame.draw.rect(screen, (0, 255, 0), (x, y, fill, bar_height))
        pygame.draw.rect(screen, (0, 0, 0), (x, y, bar_width, bar_height), 1)
