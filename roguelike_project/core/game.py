import pygame
from entities.player import Player
from entities.obstacle import Obstacle
from utils.loader import load_image
from ui.menu import Menu

class Game:
    def __init__(self, screen):
        self.screen = screen
        self.running = True
        self.background = load_image("assets/tiles/floor.png", (1200, 800))
        self.collision_mask = load_image("assets/tiles/floor_collision_mask.png", (1200, 800))
        self.player = Player(800, 600)
        self.obstacles = [
            Obstacle(300, 700),
            Obstacle(600, 725),
        ]
        self.show_menu = False
        self.menu = Menu(["Cambiar personaje", "Salir"])

    def handle_events(self):
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                self.running = False

            elif event.type == pygame.KEYDOWN:
                if event.key == pygame.K_ESCAPE:
                    self.show_menu = not self.show_menu
                elif self.show_menu:
                    result = self.menu.handle_input(event)
                    if result:
                        self.execute_menu_option(result)

        if not self.show_menu:
            keys = pygame.key.get_pressed()
            dx = dy = 0
            if keys[pygame.K_UP]: dy = -1
            if keys[pygame.K_DOWN]: dy = 1
            if keys[pygame.K_LEFT]: dx = -1
            if keys[pygame.K_RIGHT]: dx = 1
            self.player.move(dx, dy, self.collision_mask, self.obstacles)

    def update(self):
        pass

    def render(self):
        self.screen.blit(self.background, (0, 0))
        for obstacle in self.obstacles:
            obstacle.render(self.screen)
        self.player.render(self.screen)

        if self.show_menu:
            self.menu.draw(self.screen)

        pygame.display.flip()

    def execute_menu_option(self, selected):
        if selected == "Cambiar personaje":
            print("🔁 Cambiar personaje (funcionalidad pendiente)")
            self.show_menu = False
        elif selected == "Salir":
            self.running = False
