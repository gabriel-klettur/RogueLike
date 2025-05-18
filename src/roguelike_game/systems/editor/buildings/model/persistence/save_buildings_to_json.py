
# Path: src/roguelike_game/systems/editor/buildings/model/persistence/save_buildings_to_json.py
import os
import json
from typing import Dict, Tuple, Optional
from roguelike_engine.config import BUILDINGS_DATA_PATH
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_game.systems.z_layer.persistence import inject_z_into_json

def save_buildings_to_json(
    buildings,
    filepath: Optional[str] = None,
    z_state=None,
    zone_offsets: Optional[Dict[str, Tuple[int, int]]] = None
):
    """
    Guarda la lista de buildings en un JSON.
    - Si `filepath` es proporcionado, se usa esa ruta; si no, BUILDINGS_DATA_PATH.
    - Si se proporciona `z_state`, inyecta la capa Z de cada edificio.
    - Si cada edificio tiene `b.zone` y `zone_offsets` está definido,
      calcula y guarda `rel_tile_x`, `rel_tile_y`.
    """
    target = filepath or BUILDINGS_DATA_PATH
    data = []

    for b in buildings:
        try:
            # Datos base
            building_data = {
                "x": int(b.x),
                "y": int(b.y),
                "image_path": b.image_path,
                "solid": b.solid,
                "scale": [b.image.get_width(), b.image.get_height()],
                "original_scale": list(b.original_scale) if getattr(b, "original_scale", None) else None,
                "split_ratio": round(b.split_ratio, 3),
                "z_bottom": b.z_bottom,
                "z_top": b.z_top,
            }

            # Inyectar Z si corresponde
            if z_state:
                building_data["z"] = inject_z_into_json(b, z_state)

            # Relativos por zona en píxeles
            if getattr(b, "zone", None) and zone_offsets and b.zone in zone_offsets:
                ox, oy = zone_offsets[b.zone]
                origin_px_x = ox * TILE_SIZE
                origin_px_y = oy * TILE_SIZE
                building_data.update({
                    "zone": b.zone,
                    "rel_x": int(b.x - origin_px_x),
                    "rel_y": int(b.y - origin_px_y),
                })

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