# roguelike_project/systems/editor/json_handler.py

import json
import os

from roguelike_project.systems.z_layer.persistence import inject_z_into_json

def save_buildings_to_json(buildings, filepath, z_state=None):
    """
    Guarda la lista de buildings en un archivo JSON.
    Si se proporciona `z_state`, guarda también la capa Z de cada edificio.
    """
    data = []

    for b in buildings:
        try:
            building_data = {
                "x": int(b.x),
                "y": int(b.y),
                "image_path": b.image_path,
                "solid": b.solid,
                "scale": [b.image.get_width(), b.image.get_height()],
                "original_scale": list(b.original_scale) if b.original_scale else None
            }

            # 🆕 Agregar Z si se pasa `z_state`
            if z_state:
                building_data["z"] = inject_z_into_json(b, z_state)

            data.append(building_data)

        except Exception as e:
            print(f"⚠️ Error al procesar un edificio: {e}")

    if not data:
        print("⚠️ No se encontraron edificios válidos para guardar.")
        return

    os.makedirs(os.path.dirname(filepath), exist_ok=True)

    with open(filepath, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4)

    print(f"✅ {len(data)} edificios guardados en {filepath}")
