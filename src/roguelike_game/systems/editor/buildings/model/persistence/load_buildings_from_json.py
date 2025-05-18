
# Path: src/roguelike_game/systems/editor/buildings/model/persistence/load_buildings_from_json.py
import os
import json
from typing import Dict, Tuple, Optional, List
from roguelike_game.systems.z_layer.persistence import extract_z_from_json
from roguelike_engine.config_tiles import TILE_SIZE

def load_buildings_from_json(
    filepath: str,
    building_class,
    z_state=None,
    zone_offsets: Optional[Dict[str, Tuple[int, int]]] = None
) -> List:
    """
    Carga edificios desde un archivo JSON.
    - Si `z_state` se proporciona, asigna la capa Z.
    - Si `zone_offsets` se proporciona y la entrada JSON tiene `zone` + `rel_tile_*`,
      recalibra las posiciones absolutas de cada edificio.
    """
    if not os.path.exists(filepath):
        print(f"⚠️ Archivo no encontrado: {filepath}")
        return []

    with open(filepath, "r", encoding="utf-8") as f:
        try:
            data = json.load(f)
        except json.JSONDecodeError as e:
            print(f"❌ Error al leer JSON: {e}")
            return []

    buildings = []

    for entry in data:
        try:
            print(f"📥 Entrada cruda desde JSON: {entry}")

            # Creación base usando coordenadas absolutas (legacy)
            b = building_class(
                x=entry.get("x", 0),
                y=entry.get("y", 0),
                image_path=entry["image_path"],
                solid=entry.get("solid", True),
                scale=tuple(entry["scale"]) if "scale" in entry else None,
                split_ratio=entry.get("split_ratio", 0.5),
                z_bottom=entry.get("z_bottom"),
                z_top=entry.get("z_top"),
            )

            # Aplicar capa Z
            if z_state:
                extract_z_from_json(entry, z_state, b)

            # Recalibrar posición si hay datos relativos
            zone = entry.get("zone")
            if zone and zone_offsets and "rel_x" in entry and "rel_y" in entry:
                ox, oy = zone_offsets.get(zone, (0, 0))
                origin_px_x = ox * TILE_SIZE
                origin_px_y = oy * TILE_SIZE
                b.x = origin_px_x + entry["rel_x"]
                b.y = origin_px_y + entry["rel_y"]
                b.zone  = zone
                b.rel_x = entry["rel_x"]
                b.rel_y = entry["rel_y"]
            else:
                # Legacy: sin metadata de zona
                b.zone       = None
                b.rel_tile_x = None
                b.rel_tile_y = None

            # Restaurar escala original si estaba en JSON
            if entry.get("original_scale"):
                b.original_scale = tuple(entry["original_scale"])

            buildings.append(b)

        except Exception as e:
            print(f"⚠️ Error al crear edificio desde entrada JSON: {e}")

    print(f"✅ {len(buildings)} edificios cargados desde: {filepath}")
    return buildings