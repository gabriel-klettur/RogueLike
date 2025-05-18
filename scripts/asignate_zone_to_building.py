#!/usr/bin/env python3
import os
import json
import pygame

from roguelike_engine.config import BUILDINGS_DATA_PATH
from roguelike_game.entities.buildings.building import Building
from roguelike_game.systems.editor.buildings.utils.zone_helpers import assign_zone_and_relatives

def main():
    # Inicializar pygame para poder cargar imágenes
    pygame.init()
    # Dummy display para que convert_alpha() funcione
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
        # Solo procesamos las entradas que aún no tienen zona
        if not entry.get('zone'):
            # Leemos las coordenadas absolutas antiguas
            abs_x = entry.get('x', 0)
            abs_y = entry.get('y', 0)

            # Creamos el Building con rel_x=abs_x, rel_y=abs_y y sin zone
            b = Building(
                rel_x=abs_x,
                rel_y=abs_y,
                image_path=entry['image_path'],
                solid=entry.get('solid', True),
                scale=tuple(entry['scale']) if entry.get('scale') else None,
                split_ratio=entry.get('split_ratio', 0.5),
                z_bottom=entry.get('z_bottom'),
                z_top=entry.get('z_top'),
            )
            # Esto asigna b.zone, b.rel_x y b.rel_y correctamente
            assign_zone_and_relatives(b)

            # Actualizamos el JSON con la nueva zona y coords relativas
            entry['zone']  = b.zone
            entry['rel_x'] = b.rel_x
            entry['rel_y'] = b.rel_y
            # Eliminamos los campos antiguos x,y
            entry.pop('x', None)
            entry.pop('y', None)

            changed = True
            print(f"✅ Asignada zona '{b.zone}' y relativos ({b.rel_x}, {b.rel_y}) a '{b.image_path}'")

    if not changed:
        print("ℹ️  Ningún edificio sin zona encontrado.")
        return

    # Hacer backup y guardar los cambios
    backup = filepath + '.bak'
    os.replace(filepath, backup)
    with open(filepath, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=4)

    print(f"✅ Cambios guardados en {filepath} (respaldo en {backup})")

if __name__ == '__main__':
    main()
