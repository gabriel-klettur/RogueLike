
# Path: src/roguelike_game/systems/editor/buildings/model/persistence/json_handler.py
import os
import json

from roguelike_engine.config import BUILDINGS_DATA_PATH
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_game.systems.z_layer.persistence import inject_z_into_json

def save_buildings_to_json(buildings, filepath: str = None, z_state=None, zone: str = None, zone_offset: tuple[int, int] = None):
    """
    Guarda la lista de buildings en un JSON.
    - Si `filepath` es proporcionado, se usa esa ruta.
    - Si no, se usa BUILDINGS_DATA_PATH de la configuración.
    - Si se proporciona `z_state`, inyecta también la capa Z de cada edificio.
    - Si se pasa `zone_offsets`, para CADA edificio detecta su zona y calcula rel_tile_*.
    """
    target = filepath or BUILDINGS_DATA_PATH
    data = []

    for b in buildings:
        try:
            building_data = {
                "x": int(b.x),
                "y": int(b.y),
                **(
                    {
                        "zone": zone,
                        "rel_tile_x": int(b.x // TILE_SIZE) - zone_offset[0],
                        "rel_tile_y": int(b.y // TILE_SIZE) - zone_offset[1],
                    }
                    if zone is not None and zone_offset is not None
                    else {}
                ),
                "image_path": b.image_path,
                "solid": b.solid,
                "scale": [b.image.get_width(), b.image.get_height()],
                "original_scale": list(b.original_scale) if getattr(b, "original_scale", None) else None,
                "split_ratio": round(b.split_ratio, 3),
                "z_bottom": b.z_bottom,
                "z_top": b.z_top,
            }
            if z_state:
                building_data["z"] = inject_z_into_json(b, z_state)
            data.append(building_data)
        except Exception as e:
            print(f"⚠️ Error al procesar un edificio: {e}")

    if not data:
        print("⚠️ No se encontraron edificios válidos para guardar.")
        return

    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4)

    print(f"✅ {len(data)} edificios guardados en {target}")