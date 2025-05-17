#!/usr/bin/env python3
import os
import json
import pygame

from roguelike_engine.config import BUILDINGS_DATA_PATH
from roguelike_game.entities.buildings.building import Building
from roguelike_game.systems.editor.buildings.utils.zone_helpers import assign_zone_and_relatives


def main():
    # Inicializar pygame para cargar imágenes
    pygame.init()
    # Dummy display para convert_alpha
    pygame.display.set_mode((1, 1))

    filepath = BUILDINGS_DATA_PATH
    if not os.path.isfile(filepath):
        print(f"⚠️  Archivo no encontrado: {filepath}")
        return

    # Cargar datos existentes
    with open(filepath, 'r', encoding='utf-8') as f:
        data = json.load(f)

    changed = False
    for entry in data:
        if not entry.get('zone'):
            # Instanciar Building para calcular zona y relativos
            b = Building(
                x=entry.get('x', 0),
                y=entry.get('y', 0),
                image_path=entry['image_path'],
                solid=entry.get('solid', True),
                scale=tuple(entry['scale']) if entry.get('scale') else None,
                split_ratio=entry.get('split_ratio', 0.5),
                z_bottom=entry.get('z_bottom'),
                z_top=entry.get('z_top'),
            )
            assign_zone_and_relatives(b)
            entry['zone'] = b.zone
            entry['rel_tile_x'] = b.rel_tile_x
            entry['rel_tile_y'] = b.rel_tile_y
            changed = True
            print(f"✅ Asignada zona '{b.zone}' y relativos ({b.rel_tile_x}, {b.rel_tile_y}) a '{b.image_path}'")

    if not changed:
        print("ℹ️  Ningún edificio sin zona encontrado.")
        return

    # Hacer backup y guardar
    backup = filepath + '.bak'
    os.replace(filepath, backup)
    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=4)

    print(f"✅ Cambios guardados en {filepath} (respaldo en {backup})")


if __name__ == '__main__':
    main()
