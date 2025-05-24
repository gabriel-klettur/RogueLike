# src/roguelike_game/systems/editor/buildings/model/persistence/save_buildings_to_json.py

import os
import json
from typing import Dict, Tuple, Optional
from roguelike_engine.config.config import BUILDINGS_DATA_PATH
from roguelike_game.systems.z_layer.persistence import inject_z_into_json

def save_buildings_to_json(
    buildings,
    filepath: Optional[str] = None,
    z_state=None,
    zone_offsets: Optional[Dict[str, Tuple[int, int]]] = None,
    **kwargs
):
    """
    Guarda la lista de buildings en un JSON usando coordenadas relativas.
    - Si `filepath` es proporcionado, se usa esa ruta; si no, BUILDINGS_DATA_PATH.
    - Si se proporciona `z_state`, inyecta la capa Z de cada edificio.
    """
    target = filepath or BUILDINGS_DATA_PATH
    data = []

    for b in buildings:
        try:
            building_data = {
                "zone": b.zone,
                "rel_x": int(b.rel_x),
                "rel_y": int(b.rel_y),
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
