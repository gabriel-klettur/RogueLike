# Path: src/roguelike_game/config/spells_config.py
import json
from pathlib import Path

# Ruta al directorio raíz del proyecto
BASE_DIR = Path(__file__).resolve().parents[3]
# Cargar configuración de hechizos
with open(BASE_DIR / "data" / "spells.json", "r") as f:
    SPELLS = json.load(f)