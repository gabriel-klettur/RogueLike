

# Path: src/roguelike_game/systems/editor/buildings/model/persistence/load_buildings_from_json.py
import json
import os

from roguelike_game.systems.z_layer.persistence import extract_z_from_json

def load_buildings_from_json(filepath, building_class, z_state=None):
    """
    Carga edificios desde un archivo JSON.
    Si se proporciona `z_state`, también asigna la capa Z.
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
            b = building_class(
                x=entry["x"],
                y=entry["y"],
                image_path=entry["image_path"],
                solid=entry.get("solid", True),
                scale=tuple(entry["scale"]) if "scale" in entry else None,                                
                split_ratio=entry.get("split_ratio", 0.5),
                z_bottom=entry.get("z_bottom"),
                z_top=entry.get("z_top"),
            )

            # 🆕 Asignar capa Z si corresponde
            if z_state:
                extract_z_from_json(entry, z_state, b)

            # 🆕 Restaurar escala original si se guardó
            if "original_scale" in entry and entry["original_scale"]:
                b.original_scale = tuple(entry["original_scale"])

            buildings.append(b)

        except Exception as e:
            print(f"⚠️ Error al crear edificio desde entrada JSON: {e}")

    print(f"✅ {len(buildings)} edificios cargados desde: {filepath}")
    return buildings