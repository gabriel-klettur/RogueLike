# network/client.py

import threading
import websocket
import json
import time
import uuid

class WebSocketClient:
    def __init__(self, url, player):
        self.url = url
        self.player = player
        self.id = str(uuid.uuid4())  # Este ID se usará para identificarte
        self.ws = None
        self.running = True
        self.remote_players = {}  # Jugadores remotos

    def start(self):
        def run():
            try:
                self.ws = websocket.WebSocket()
                self.ws.connect(self.url)
                print("✅ Conectado al servidor WebSocket")

                threading.Thread(target=self.send_loop, daemon=True).start()
                threading.Thread(target=self.receive_loop, daemon=True).start()

            except Exception as e:
                print(f"❌ Error al conectar: {e}")

        threading.Thread(target=run, daemon=True).start()

    def send_loop(self):
        while self.running:
            try:
                data = {
                    "id": self.id,
                    "x": self.player.x,
                    "y": self.player.y,
                }
                self.ws.send(json.dumps(data))
            except Exception as e:
                print(f"❌ Error al enviar datos: {e}")
                self.running = False
                break
            time.sleep(1)

    def receive_loop(self):
        while self.running:
            try:
                message = self.ws.recv()
                print(f"📨 Mensaje bruto recibido: {message}")
                data = json.loads(message)

                # Si no viene con clave "remote_players", asumimos que data YA ES el diccionario de jugadores
                self.remote_players = data  # ✅ CAMBIO CRUCIAL

                print(f"📥 Recibido: {list(self.remote_players.keys())}")
            except Exception as e:
                print(f"❌ Error al recibir datos: {e}")
                self.running = False
                break
