
from src.roguelike_game.network.client import WebSocketClient
from src.roguelike_engine.config import WEBSOCKET_URL

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
            self.state.websocket_connected = False
            print(f"❌ Error al conectar WebSocket: {e}")

    def disconnect(self):
        if self.websocket:
            try:
                self.websocket.running = False
                self.websocket.ws.close()
                print("🧯 WebSocket desconectado.")
            except Exception as e:
                print(f"❌ Error al cerrar WebSocket: {e}")
        self.state.websocket = None
        self.state.websocket_connected = False
