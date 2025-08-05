import json
from pathlib import Path
import logging
logger = logging.getLogger(__name__)

def main():
    # Ruta al JSON de monstruos
    path = Path(__file__).parent.parent / "data" / "entities" / "monsters.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(data, dict):
        monsters = data.values()
    else:
        monsters = data

    # Migrar death_sprite dentro de sprites
    for m in monsters:
        sprites = m.setdefault("sprites", {})
        if "death_sprite" in m:
            sprites["death"] = m.pop("death_sprite")

    # Escribir JSON actualizado
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")
    logger.info("JSON migrado: death_sprite → sprites['death'] en monsters.json")

if __name__ == "__main__":
    main()
