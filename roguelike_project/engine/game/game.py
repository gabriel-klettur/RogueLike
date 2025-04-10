# roguelike_project/engine/game/game.py

import pygame

from roguelike_project.engine.game.input.events import handle_events
from roguelike_project.engine.game.systems.state import GameState
from roguelike_project.engine.game.systems.map_manager import build_map
from roguelike_project.engine.game.systems.entity_loader import load_entities
from roguelike_project.engine.game.systems.network_manager import NetworkManager
from roguelike_project.ui.menus.menu import Menu
from roguelike_project.engine.camera import Camera
from roguelike_project.engine.game.render.render import Renderer
from roguelike_project.engine.game.update_manager import update_game
from roguelike_project.config import SCREEN_WIDTH, SCREEN_HEIGHT, FONT_NAME, FONT_SIZE

class Game:
    def __init__(self, screen):
        self.screen = screen
        self.clock = pygame.time.Clock()
        self.font = pygame.font.SysFont(FONT_NAME, FONT_SIZE)
        self.camera = Camera(SCREEN_WIDTH, SCREEN_HEIGHT)
        
        self._init_state()
        self._init_map()
        self._init_entities()
        self._init_systems()

    def _init_state(self):
        self.state = GameState(
            screen=self.screen,
            background=None,
            player=None,
            obstacles=None,
            buildings=None,
            camera=self.camera,
            clock=self.clock,
            font=self.font,
            menu=None,
            tiles=None,
            enemies=None
        )
        self.state.running = True
        
    def _init_map(self):
        self.map_data, self.state.tile_map = build_map()
        self.state.tiles = [tile for row in self.state.tile_map for tile in row]
        
    def _init_entities(self):
        player, obstacles, buildings, enemies = load_entities()
        self.state.player = player
        self.state.obstacles = obstacles
        self.state.buildings = buildings
        self.state.enemies = enemies
        
    def _init_systems(self):
        self.renderer = Renderer()
        self.state.player.renderer.state = self.state
        self.state.menu = Menu(self.state)
        self.state.remote_entities = {}
        self.state.show_menu = False
        self.state.mode = "local"

        self._init_combat_system()  # ✅ nuevo sistema de combate global

        self.network = NetworkManager(self.state)
        if self.state.mode == "online":
            self.network.connect()

    def _init_combat_system(self):
        from roguelike_project.entities.combat.combat_system import CombatSystem
        self.state.combat = CombatSystem(self.state)

    def handle_events(self):
        handle_events(self.state)

    def update(self):
        update_game(self.state)

    def render(self, perf_log=None):
        self.renderer.render_game(self.state, perf_log)

    def run(self):
        while self.state.running:
            self.handle_events()
            self.update()
            self.render()
            self.state.clock.tick(60)

    def quit(self):
        if hasattr(self, 'network'):
            self.network.disconnect()
        pygame.quit()
