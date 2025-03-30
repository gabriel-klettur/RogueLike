import pygame
from .events import handle_events
from .render import render_game
from .state import GameState
from entities.player.base import Player
from entities.obstacle import Obstacle
from utils.loader import load_image
from ui.menu import Menu
from core.camera import Camera

class Game:
    def __init__(self, screen):
        self.state = GameState(
            screen=screen,
            background=load_image("assets/tiles/floor.png", (2408, 1024)),
            collision_mask=load_image("assets/tiles/floor_collision_mask.png", (2408, 1024)),
            player=Player(600, 600),
            obstacles=[
                Obstacle(300, 700),
                Obstacle(600, 725),
            ],
            camera=Camera(1200, 800),
            clock=pygame.time.Clock(),
            font=pygame.font.SysFont("Arial", 18),
            menu=Menu(["Cambiar personaje", "Salir"])
        )

    def handle_events(self):
        handle_events(self.state)

    def update(self):
        self.state.camera.update(self.state.player)

    def render(self):
        render_game(self.state)
