import pygame
from roguelike_project.core.game.logic.events import handle_events
from roguelike_project.core.game.render.render import render_game
from roguelike_project.core.game.logic.state import GameState

from roguelike_project.entities.player.base import Player

from roguelike_project.ui.menu import Menu
from roguelike_project.core.camera import Camera
from roguelike_project.network.client import WebSocketClient

from roguelike_project.map.map_generator import generate_dungeon_map, merge_handmade_with_generated
from roguelike_project.map.lobby_map import LOBBY_MAP
from roguelike_project.map.map_loader import load_map_from_text

from roguelike_project.entities.setup.load_obstacles import load_obstacles
from roguelike_project.entities.setup.load_enemies import load_enemies
from roguelike_project.entities.setup.load_buildings import load_buildings

class Game:
    def __init__(self, screen):
        dungeon = generate_dungeon_map(60, 40)        
        self.map_data = merge_handmade_with_generated(LOBBY_MAP, dungeon, offset_x=5, offset_y=5)                            
        self.tiles = load_map_from_text(self.map_data)
        self.state = GameState(
            screen=screen,
            background=None,
            player=Player(600, 600),
            
            obstacles=load_obstacles(),
            buildings=load_buildings(),
            enemies=load_enemies(),

            camera=Camera(1200, 800),
            clock=pygame.time.Clock(),
            font=pygame.font.SysFont("Arial", 18),
            menu=None,
            tiles=self.tiles
        )

        self.state.menu = Menu(state=self.state)
        self.state.remote_entities = {}

        if self.state.mode == "online":
            try:
                self.websocket = WebSocketClient("ws://localhost:8000/ws", self.state.player)
                self.websocket.start()
                self.state.websocket_connected = True
                print("✅ Conectado al servidor WebSocket")
            except Exception as e:
                print(f"❌ Error al conectar WebSocket: {e}")
                self.state.websocket_connected = False

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
