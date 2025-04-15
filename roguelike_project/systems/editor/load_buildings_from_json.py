import json
import os

def load_buildings_from_json(filepath, building_class):
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
            b = building_class(
                x=entry["x"],
                y=entry["y"],
                image_path=entry["image_path"],
                solid=entry.get("solid", True),
                scale=tuple(entry["scale"]) if "scale" in entry else None
            )
            # 🆕 Asignar escala original si existe
            if "original_scale" in entry and entry["original_scale"]:
                b.original_scale = tuple(entry["original_scale"])
            buildings.append(b)
        except Exception as e:
            print(f"⚠️ Error al crear edificio desde entrada JSON: {e}")

    print(f"✅ {len(buildings)} edificios cargados desde: {filepath}")
    return buildings
