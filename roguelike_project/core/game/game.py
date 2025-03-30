import pygame
from roguelike_project.entities.player.base import Player
from roguelike_project.entities.obstacle import Obstacle
from roguelike_project.utils.loader import load_image
from roguelike_project.ui.menu import Menu
from roguelike_project.core.camera import Camera

class Game:
    def __init__(self, screen):
        self.screen = screen
        self.running = True
        self.background = load_image("assets/tiles/floor.png", (2408, 1024))
        self.collision_mask = load_image("assets/tiles/floor_collision_mask.png", (2408, 1024))
        self.player = Player(600, 600)
        self.obstacles = [
            Obstacle(300, 700),
            Obstacle(600, 725),
        ]
        self.camera = Camera(1200, 800)
        self.clock = pygame.time.Clock()
        self.font = pygame.font.SysFont("Arial", 18)
        self.show_menu = False
        self.menu = Menu(["Cambiar personaje", "Salir"])

    def handle_events(self):
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                self.running = False

            elif event.type == pygame.KEYDOWN:
                if event.key == pygame.K_ESCAPE:
                    self.show_menu = not self.show_menu
                elif event.key == pygame.K_q:
                    self.player.restore_all()
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
        self.camera.update(self.player)

    def render(self):
        self.screen.fill((0, 0, 0))
        self.screen.blit(self.background, self.camera.apply((0, 0)))

        for obstacle in self.obstacles:
            obstacle.render(self.screen, self.camera)

        self.player.render(self.screen, self.camera)
        self.player.render_hud(self.screen, self.camera)

        if self.show_menu:
            self.menu.draw(self.screen)

        # ✅ Mostrar FPS al final, con fondo para mayor visibilidad
        fps = self.clock.get_fps()
        fps_text = self.font.render(f"FPS: {int(fps)}", True, (255, 255, 255))
        pygame.draw.rect(self.screen, (0, 0, 0), (8, 8, 60, 22))  # Fondo negro
        self.screen.blit(fps_text, (10, 10))

        pygame.display.flip()

    def execute_menu_option(self, selected):
        if selected == "Cambiar personaje":
            new_name = "valkyria" if self.player.character_name == "first_hero" else "first_hero"
            self.player.change_character(new_name)
            print(f"✅ Cambiado a personaje: {new_name}")
            self.show_menu = False
        elif selected == "Salir":
            self.running = False
