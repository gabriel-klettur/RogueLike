# src.roguelike_project/engine/game/systems/map/map_exporter.py
import os
import re

def save_map_with_autoname(map_data, directory="debug_maps"):
    # Asegurar que exista la carpeta
    os.makedirs(directory, exist_ok=True)

    # Buscar el último archivo creado
    existing = [
        f for f in os.listdir(directory)
        if re.match(r"map_\d{3}\.txt", f)
    ]

    if existing:
        numbers = [int(re.findall(r"\d{3}", f)[0]) for f in existing]
        next_number = max(numbers) + 1
    else:
        next_number = 1

    filename = f"map_{next_number:03}.txt"
    filepath = os.path.join(directory, filename)

    # Guardar el mapa
    with open(filepath, "w", encoding="utf-8") as f:
        for row in map_data:
            if isinstance(row, list):
                f.write("".join(row) + "\n")
            else:
                f.write(row + "\n")

    print(f"📝 Mapa guardado como: {filepath}")
    return filename  # Devolvemos el nombre para overlay