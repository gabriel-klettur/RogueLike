import json
from pathlib import Path
import logging
logger = logging.getLogger(__name__)

def main():
    # Ruta al JSON de monstruos
    path = Path(__file__).parent.parent / "data" / "entities" / "monsters.json"
    data = json.loads(path.read_text(encoding="utf-8"))

    # Direcciones canónicas
    directions = ["s", "se", "e", "ne", "n", "nw", "w", "sw"]
    base_map = {"s": "down", "e": "right", "n": "up", "w": "left"}
    categories = ["idle", "chase", "attack", "death", "damage", "casting"]

    for key, m in data.items():
        old_sprites = m.get("sprites", {}) or {}
        # Extraer propiedades de datos y eliminarlas del nivel superior
        scale = m.pop("scale", None)
        death_scale = m.pop("death_scale", None)
        tint = m.pop("tint", None)

        # Construir assets reorganizados
        new_assets = {}
        for cat in categories:
            new_assets[cat] = {d: None for d in directions}
            if cat == "death":
                # Muerte sólo en 's'
                new_assets[cat]["s"] = old_sprites.get("death")
            else:
                for d, flat in base_map.items():
                    key_name = flat if cat == "idle" else f"{cat}_{flat}"
                    new_assets[cat][d] = old_sprites.get(key_name)
        
        # data_assets con escala y tint
        data_assets = {
            "scale": scale,
            "death_scale": death_scale,
            "tint": tint
        }

        # Asignar nueva estructura
        m["sprites"] = {
            "assets": new_assets,
            "data_assets": data_assets
        }

    # Guardar JSON actualizado
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")
    logger.info("Reestructuración completada: sprites → {assets, data_assets}")

if __name__ == "__main__":
    main()
