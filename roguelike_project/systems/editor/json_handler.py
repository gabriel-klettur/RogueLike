# roguelike_project/editor/json_handler.py

import json
import os

def save_buildings_to_json(buildings, filepath):
    data = []
    for b in buildings:
        building_data = {
            "x": b.x,
            "y": b.y,
            "image_path": b.image_path,  # ← asumimos que esto existe o se lo añadiremos luego
            "solid": b.solid,
            "scale": [b.image.get_width(), b.image.get_height()]
        }
        data.append(building_data)

    os.makedirs(os.path.dirname(filepath), exist_ok=True)

    with open(filepath, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4)
    print(f"💾 Edificios guardados en: {filepath}")


def load_buildings_from_json(filepath, building_class):
    if not os.path.exists(filepath):
        print(f"⚠️ Archivo no encontrado: {filepath}")
        return []

    with open(filepath, "r", encoding="utf-8") as f:
        data = json.load(f)

    buildings = []
    for entry in data:
        b = building_class(
            x=entry["x"],
            y=entry["y"],
            image_path=entry["image_path"],
            solid=entry.get("solid", True),
            scale=tuple(entry["scale"]) if "scale" in entry else None
        )
        buildings.append(b)

    print(f"✅ Edificios cargados desde: {filepath}")
    return buildings
