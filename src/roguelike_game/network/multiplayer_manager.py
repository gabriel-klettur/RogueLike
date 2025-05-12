
# Path: src/roguelike_game/network/multiplayer_manager.py
from roguelike_game.network.client import WebSocketClient
from roguelike_engine.config import WEBSOCKET_URL

class NetworkManager:
    def __init__(self):        
        self.websocket = None
        self.connected = "Local"
        self.remote_entities = {}

    def connect(self, entities):
        try:
            self.websocket = WebSocketClient(WEBSOCKET_URL, entities.player)
            self.websocket.start()
            self.websocket_connected = True
            self.websocket = self.websocket
            print("✅ Conectado al servidor WebSocket")
        except Exception as e:
            self.websocket_connected = False
            print(f"❌ Error al conectar WebSocket: {e}")

    def disconnect(self):
        if self.websocket:
            try:
                self.websocket.running = False
                self.websocket.ws.close()
                print("🧯 WebSocket desconectado.")
            except Exception as e:
                print(f"❌ Error al cerrar WebSocket: {e}")
        self.websocket = None
        self.websocket_connected = False