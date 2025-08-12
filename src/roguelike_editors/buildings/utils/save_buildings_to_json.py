import os
import json
from typing import Dict, Tuple, Optional
from roguelike_engine.config.config import BUILDINGS_DATA_PATH
from roguelike_engine.z_layer.persistence import inject_z_into_json
import logging
logger = logging.getLogger(__name__)

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
                "collider_scope": getattr(b, "collider_scope", "CG"),
            }

            if z_state:
                building_data["z"] = inject_z_into_json(b, z_state)

            # Persistir override de colisiones por instancia si el alcance es CU
            if building_data.get("collider_scope") == "CU" and getattr(b, "collision_map", None):
                rows = len(b.collision_map)
                cols = len(b.collision_map[0]) if rows > 0 else 0
                building_data["collision_override"] = {
                    "width": cols,
                    "height": rows,
                    "collision": b.collision_map,
                }

            data.append(building_data)

        except Exception as e:
            logger.error(f"⚠️ Error al procesar un edificio: {e}")

    if not data:
        logger.warning("⚠️ No se encontraron edificios válidos para guardar.")
        return

    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4)

    logger.info(f"✅ {len(data)} edificios guardados en {target}")