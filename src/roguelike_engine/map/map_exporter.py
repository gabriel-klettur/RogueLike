# src/roguelike_project/engine/game/systems/map/map_exporter.py
import os
import re
from roguelike_engine.config import MAP_DEBUG_DIR

def save_map_with_autoname(map_data):
    # Asegurar que exista la carpeta
    os.makedirs(MAP_DEBUG_DIR, exist_ok=True)

    # Buscar archivos existentes
    existing = [
        f for f in os.listdir(MAP_DEBUG_DIR)
        if re.match(r"map_\d{3}\.txt", f)
    ]

    if existing:
        numbers = [int(re.findall(r"\d{3}", f)[0]) for f in existing]
        next_number = max(numbers) + 1
    else:
        next_number = 1

    filename = f"map_{next_number:03}.txt"
    filepath = os.path.join(MAP_DEBUG_DIR, filename)

    # Guardar
    with open(filepath, "w", encoding="utf-8") as f:
        for row in map_data:
            f.write("".join(row) + "\n" if isinstance(row, list) else row + "\n")

    print(f"📝 Mapa guardado como: {filepath}")
    return filename
