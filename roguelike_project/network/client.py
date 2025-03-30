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
        self.id = str(uuid.uuid4())
        self.ws = None
        self.running = True

    def start(self):
        def run():
            try:
                self.ws = websocket.WebSocket()
                self.ws.connect(self.url)
                print("✅ Conectado al servidor WebSocket")

                # Lanzar hilo de envío periódico
                threading.Thread(target=self.send_loop, daemon=True).start()

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
            time.sleep(1)  # ⏱️ Envía cada 1 segundo (ajustable)
