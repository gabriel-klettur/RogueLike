# src/roguelike_game/systems/editor/buildings/model/persistence/load_buildings_from_json.py

import os
import json
from typing import List
from roguelike_engine.config.config import BUILDINGS_DATA_PATH, BUILDINGS_COLLISIONS_DATA_PATH
from roguelike_game.systems.z_layer.persistence import extract_z_from_json

from roguelike_game.entities.buildings.building import Building

def load_buildings_from_json(
    z_state=None
) -> List:
    """
    Carga edificios desde JSON usando coordenadas relativas.
    - Si `z_state` se proporciona, inyecta la capa Z.
    """
    if not os.path.exists(BUILDINGS_DATA_PATH):
        print(f"⚠️ Archivo no encontrado: {BUILDINGS_DATA_PATH}")
        return []

    # Cargar colisiones de buildings
    try:
        with open(BUILDINGS_COLLISIONS_DATA_PATH, 'r', encoding='utf-8') as cf:
            collisions_data = json.load(cf)
    except Exception:
        collisions_data = {}

    with open(BUILDINGS_DATA_PATH, "r", encoding="utf-8") as f:
        try:
            data = json.load(f)
        except json.JSONDecodeError as e:
            print(f"❌ Error al leer JSON: {e}")
            return []

    buildings: List[Building] = []

    for entry in data:
        try:
            #print(f"📥 Entrada cruda desde JSON: {entry}")

            b = Building(
                rel_x=entry.get("rel_x", 0),
                rel_y=entry.get("rel_y", 0),
                image_path=entry["image_path"],
                solid=entry.get("solid", True),
                scale=tuple(entry["scale"]) if "scale" in entry else None,
                split_ratio=entry.get("split_ratio", 0.5),
                z_bottom=entry.get("z_bottom"),
                z_top=entry.get("z_top"),
            )

            # Inicializar collision_map
            coll_entry = collisions_data.get(entry.get("image_path"))
            if coll_entry and "collision" in coll_entry:
                b.collision_map = coll_entry["collision"]
            else:
                from roguelike_engine.config.config_tiles import TILE_SIZE
                w = b.image.get_width() // TILE_SIZE
                h = b.image.get_height() // TILE_SIZE
                b.collision_map = [["." for _ in range(w)] for _ in range(h)]

            # Aplicar capa Z
            if z_state:
                extract_z_from_json(entry, z_state, b)

            # Asignar zona si viene en JSON
            if entry.get("zone"):
                b.zone = entry["zone"]

            # Restaurar escala original si estaba en JSON
            if entry.get("original_scale"):
                b.original_scale = tuple(entry["original_scale"])

            buildings.append(b)

        except Exception as e:
            print(f"⚠️ Error al crear edificio desde entrada JSON: {e}")

    print(f"[Buildings][Cargando Edificios] {len(buildings)} edificios cargados desde: [{BUILDINGS_DATA_PATH}]")
    return buildings
