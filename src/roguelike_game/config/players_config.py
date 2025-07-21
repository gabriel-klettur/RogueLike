from pathlib import Path
import json

# Ruta al directorio raíz del proyecto
top = Path(__file__).resolve().parents[3]
# Cargar configuración de jugadores
with open(top / "data" / "entities" / "players.json", "r", encoding="utf-8") as f:
    PLAYER_CFG = json.load(f)

# Estadísticas específicas por clase de jugador
PLAYER_STATS = PLAYER_CFG.get("PLAYER_STATS", {})