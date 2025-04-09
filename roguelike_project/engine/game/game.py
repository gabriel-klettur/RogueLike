import pygame
from roguelike_project.engine.game.input.events import handle_events
from roguelike_project.engine.game.systems.state import GameState
from roguelike_project.engine.game.systems.map_manager import build_map
from roguelike_project.engine.game.systems.entity_loader import load_entities
from roguelike_project.engine.game.systems.network_manager import NetworkManager
from roguelike_project.ui.menus.menu import Menu
from roguelike_project.engine.camera import Camera
from roguelike_project.engine.game.render.render import Renderer
from roguelike_project.config import SCREEN_WIDTH, SCREEN_HEIGHT, FONT_NAME, FONT_SIZE

class Game:
    def __init__(self, screen):
        # Initialize essential components first
        self.screen = screen
        self.clock = pygame.time.Clock()
        self.font = pygame.font.SysFont(FONT_NAME, FONT_SIZE)
        self.camera = Camera(SCREEN_WIDTH, SCREEN_HEIGHT)

        # Initialize state with minimum required attributes
        self.state = GameState(
            screen=screen,
            background=None,
            player=None,  # Will be set after loading
            obstacles=None,
            buildings=None,
            camera=self.camera,
            clock=self.clock,
            font=self.font,
            menu=None,  # Will be set after creation
            tiles=None,  # Deprecated (replaced by tile_map)
            enemies=None
        )        
        self.state.running = True

        # Load map and entities
        self.map_data, self.tile_map = build_map()
        player, obstacles, buildings, enemies = load_entities()

        # Update state with loaded content
        self.state.player = player
        self.state.obstacles = obstacles
        self.state.buildings = buildings
        self.state.enemies = enemies
        self.state.tile_map = self.tile_map

        # Optional: keep flat list of tiles for compatibility
        self.state.tiles = [tile for row in self.tile_map for tile in row]

        # Initialize remaining systems
        self.renderer = Renderer()
        self.state.player.renderer.state = self.state  # ✅ Conexión necesaria para láser con daño
        self.state.menu = Menu(self.state)
        self.state.remote_entities = {}
        self.state.running = True
        self.state.show_menu = False
        self.state.mode = "local"

        # Network initialization
        self.network = NetworkManager(self.state)
        if self.state.mode == "online":
            self.network.connect()

    def handle_events(self):
        handle_events(self.state)

    def update(self):
        if not self.state.running:
            return

        self.state.camera.update(self.state.player)

        solid_tiles = [tile for tile in self.state.tiles if tile.solid]
        enemies = self.state.enemies + list(self.state.remote_entities.values())

        self.state.player.update(solid_tiles, enemies)

        #for projectile in self.state.player.projectiles:
        #    projectile.update(solid_tiles=solid_tiles, enemies=enemies)

        for enemy in enemies:
            enemy.update()

        self.state.player.projectiles = [p for p in self.state.player.projectiles if p.alive]

    def render(self, perf_log=None):
        self.renderer.render_game(self.state, perf_log)

    def run(self):
        """Main game loop"""
        while self.state.running:
            self.handle_events()
            self.update()
            self.render()
            self.state.clock.tick(60)

    def quit(self):
        """Clean up resources"""
        if hasattr(self, 'network'):
            self.network.disconnect()
        pygame.quit()
