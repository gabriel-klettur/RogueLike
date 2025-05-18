#!/usr/bin/env python3
import json
import os
from roguelike_engine.config import BUILDINGS_DATA_PATH
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.config_map import ZONE_OFFSETS

def migrate():
    # 1) Leer datos actuales
    with open(BUILDINGS_DATA_PATH, 'r', encoding='utf-8') as f:
        data = json.load(f)

    new_data = []
    for entry in data:
        zone = entry.get("zone")
        if zone:
            # Offset de la zona en tiles
            ox, oy = ZONE_OFFSETS.get(zone, (0, 0))
            # Origen de la zona en píxeles
            origin_px_x = ox * TILE_SIZE
            origin_px_y = oy * TILE_SIZE

            # Recalcular basándonos en coordenadas absolutas x, y
            abs_x = entry.get("x", 0)
            abs_y = entry.get("y", 0)

            entry["rel_x"] = int(abs_x - origin_px_x)
            entry["rel_y"] = int(abs_y - origin_px_y)

        # Limpiar campos viejos
        entry.pop("rel_tile_x", None)
        entry.pop("rel_tile_y", None)
        new_data.append(entry)

    # 2) Hacer backup y escribir el nuevo JSON
    backup = BUILDINGS_DATA_PATH + ".bak"
    os.replace(BUILDINGS_DATA_PATH, backup)
    with open(BUILDINGS_DATA_PATH, 'w', encoding='utf-8') as f:
        json.dump(new_data, f, ensure_ascii=False, indent=4)

    print(f"Migración completada. Backup del original en: {backup}")

if __name__ == "__main__":
    migrate()
