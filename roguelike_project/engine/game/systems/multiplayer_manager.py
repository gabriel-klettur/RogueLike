from roguelike_project.network.client import WebSocketClient
from roguelike_project.config import WEBSOCKET_URL


class NetworkManager:
    def __init__(self, state):
        self.state = state
        self.websocket = None

    def connect(self):
        try:
            self.websocket = WebSocketClient(WEBSOCKET_URL, self.state.player)
            self.websocket.start()
            self.state.websocket_connected = True
            self.state.websocket = self.websocket
            print("✅ Conectado al servidor WebSocket")
        except Exception as e:
            print(f"❌ Error al conectar WebSocket: {e}")
            self.state.websocket_connected = False
