import pygame
from .events import handle_events
from .render import render_game
from .state import GameState
from entities.player.base import Player
from entities.obstacle import Obstacle
from utils.loader import load_image
from ui.menu import Menu
from core.camera import Camera
from network.client import WebSocketClient  # ✅ Importa el cliente WebSocket

class Game:
    def __init__(self, screen):
        self.state = GameState(
            screen=screen,
            background=load_image("assets/tiles/floor.png", (3551, 1024)),
            collision_mask=load_image("assets/tiles/floor_collision_mask.png", (3551, 1024)),
            player=Player(600, 600),
            obstacles=[
                Obstacle(300, 700),
                Obstacle(600, 725),
            ],
            camera=Camera(1200, 800),
            clock=pygame.time.Clock(),
            font=pygame.font.SysFont("Arial", 18),
            menu=None
        )

        self.state.menu = Menu(state=self.state)

        # ✅ Si el modo es online, conecta el WebSocket
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
