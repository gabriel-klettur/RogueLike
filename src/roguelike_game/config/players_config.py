from pathlib import Path
import json

# Ruta al directorio raíz del proyecto
top = Path(__file__).resolve().parents[3]
# Cargar configuración de jugadores
# Cargar configuración de jugadores en nuevo formato
with open(top / "data" / "entities" / "new_players.json", "r", encoding="utf-8") as f:
    PLAYER_CFG = json.load(f)

# Extraer clases de jugador
_CLASSES = PLAYER_CFG.get("players", {}).get("classes", {})
PLAYER_STATS = {cls: cfg.get("stats", {}) for cls, cfg in _CLASSES.items()}
PLAYER_ASSETS = {cls: cfg.get("assets", {}) for cls, cfg in _CLASSES.items()}