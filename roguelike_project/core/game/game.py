import pygame
from roguelike_project.core.game.input.events import handle_events
from roguelike_project.core.game.render.render import render_game
from roguelike_project.core.game.systems.state import GameState
from roguelike_project.core.game.systems.map_manager import build_map
from roguelike_project.core.game.systems.entity_loader import load_entities
from roguelike_project.core.game.systems.network_manager import NetworkManager
from roguelike_project.ui.menu import Menu
from roguelike_project.core.camera import Camera

from roguelike_project.config import SCREEN_WIDTH, SCREEN_HEIGHT, FONT_NAME, FONT_SIZE

class Game:
    def __init__(self, screen):
        self.map_data, self.tiles = build_map()
        player, obstacles, buildings, enemies = load_entities()

        self.state = GameState(
            screen=screen,
            background=None,
            player=player,
            obstacles=obstacles,
            buildings=buildings,
            enemies=enemies,
            camera=Camera(SCREEN_WIDTH, SCREEN_HEIGHT),
            clock=pygame.time.Clock(),
            font=pygame.font.SysFont(FONT_NAME, FONT_SIZE),
            menu=None,
            tiles=self.tiles
        )

        self.state.menu = Menu(state=self.state)
        self.state.remote_entities = {}

        self.network = NetworkManager(self.state)
        if self.state.mode == "online":
            self.network.connect()

    def handle_events(self):
        handle_events(self.state)

    def update(self):
        self.state.camera.update(self.state.player)

        solid_tiles = [tile for tile in self.state.tiles if tile.solid]
        enemies = self.state.enemies + list(self.state.remote_entities.values())

        for projectile in self.state.player.projectiles:
            projectile.update(solid_tiles=solid_tiles, enemies=enemies)

        for enemy in enemies:
            enemy.update()

        self.state.player.projectiles = [p for p in self.state.player.projectiles if p.alive]

    def render(self):
        render_game(self.state)
