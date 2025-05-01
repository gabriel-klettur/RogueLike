# Path: src/roguelike_engine/map/exporter/map_exporter.py
import os
import re
from roguelike_engine.config_map import DEBUG_MAPS_DIR

def save_map_with_autoname(map_data):
    """
    Guarda un mapa de debug con nombre autonumérico en DEBUG_MAPS_DIR.
    """
    # Asegurar que exista la carpeta de debug
    os.makedirs(DEBUG_MAPS_DIR, exist_ok=True)

    # Buscar archivos existentes que empiecen con 'map_' y terminen en '.txt'
    existing = [
        f for f in os.listdir(DEBUG_MAPS_DIR)
        if re.match(r"map_\\d{3}\\.txt", f)
    ]

    if existing:
        numbers = [int(re.findall(r"\\d{3}", f)[0]) for f in existing]
        next_number = max(numbers) + 1
    else:
        next_number = 1

    filename = f"map_{next_number:03}.txt"
    filepath = os.path.join(DEBUG_MAPS_DIR, filename)

    # Guardar el mapa
    with open(filepath, "w", encoding="utf-8") as f:
        for row in map_data:
            line = "".join(row) if isinstance(row, list) else row
            f.write(line + "\n")

    print(f"📝 Mapa de debug guardado como: {filepath}")
    return filename