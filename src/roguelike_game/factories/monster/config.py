import json
from pathlib import Path

# Carga de definiciones de monstruos desde JSON
_DATA_DIR = Path(__file__).resolve().parents[4] / "data"
with open(_DATA_DIR / "entities/monsters.json", encoding="utf-8") as f:
    MONSTER_DEFS = json.load(f)