# network/client.py
import threading
import websocket
import json
import time
import uuid
import socket

class WebSocketClient:
    def __init__(self, url, player):
        self.url = url
        self.player = player
        self.id = str(uuid.uuid4())
        self.ws = None
        self.running = True
        self.remote_players = {}
        self.send_interval = 0.05  # ✅ Reducido para mejorar rendimiento

    def start(self):
        def run():
            try:
                self.ws = websocket.WebSocket()
                self.ws.connect(self.url)
                self.ws.settimeout(1)  # ✅ Evita bloqueo indefinido
                print("✅ Conectado al servidor WebSocket")

                threading.Thread(target=self.send_loop, daemon=True).start()
                threading.Thread(target=self.receive_loop, daemon=True).start()

            except Exception as e:
                print(f"❌ Error al conectar: {e}")

        threading.Thread(target=run, daemon=True).start()

    def send_loop(self):
        prev_data = {}
        while self.running:
            try:
                data = {
                    "id": self.id,
                    "x": self.player.x,
                    "y": self.player.y,
                    "character": self.player.character_name,
                    "direction": self.player.direction,
                    "health": self.player.stats.health,
                    "mana": self.player.stats.mana,
                    "energy": self.player.stats.energy
                }
                if data != prev_data:
                    self.ws.send(json.dumps(data))
                    prev_data = data.copy()
                time.sleep(self.send_interval)
            except Exception as e:
                print(f"❌ Error al enviar datos: {e}")
                self.running = False
                break


    def receive_loop(self):
        while self.running:
            try:
                message = self.ws.recv()
                data = json.loads(message)

                # ✅ Actualización incremental
                current_ids = set(data.keys())
                for pid in list(self.remote_players.keys()):
                    if pid not in current_ids:
                        del self.remote_players[pid]

                for pid, pdata in data.items():
                    self.remote_players[pid] = pdata

            except websocket.WebSocketTimeoutException:
                continue
            except Exception as e:
                print(f"❌ Error al recibir datos: {e}")
                self.running = False
                break