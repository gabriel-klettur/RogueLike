import pygame
from .events import handle_events
from .render import render_game
from .state import GameState

from roguelike_project.entities.player.base import Player
from roguelike_project.entities.obstacle import Obstacle
from roguelike_project.utils.loader import load_image
from roguelike_project.ui.menu import Menu
from roguelike_project.core.camera import Camera
from roguelike_project.network.client import WebSocketClient
from roguelike_project.map.map_loader import load_map_from_text


class Game:
    def __init__(self, screen):
        # 🗺️ Mapa de prueba
        self.map_data = [
            "####################",
            "#..................#",
            "#..................#",
            "#.......##.........#",
            "#..................#",
            "#..................#",
            "#..........##......#",
            "#..................#",
            "#..................#",
            "#..................#",
            "#..................#",
            "#..................#",
            "####################"
        ]
        self.tiles = load_map_from_text(self.map_data)

        # 🧠 Estado inicial del juego
        self.state = GameState(
            screen=screen,
            background=load_image("assets/tiles/floor.png", (3551, 1024)),  # ⬅️ Podés reemplazar luego            
            player=Player(600, 600),
            obstacles=[
                Obstacle(300, 700),
                Obstacle(600, 725),
            ],
            camera=Camera(1200, 800),
            clock=pygame.time.Clock(),
            font=pygame.font.SysFont("Arial", 18),
            menu=None,
            tiles=self.tiles  # ✅ Se agrega tiles al estado
        )

        self.state.menu = Menu(state=self.state)
        self.state.remote_entities = {}

        # 🌐 Modo online: conecta WebSocket si aplica
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

    def render(self):
        render_game(self.state)
