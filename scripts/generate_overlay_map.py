#!/usr/bin/env python3

#Path: scripts/generate_overlay_map.py

import os
import json
from pathlib import Path

# 1) Definimos la raíz del proyecto y rutas
PROJECT_ROOT = Path(__file__).resolve().parent.parent
ASSETS_TILES = PROJECT_ROOT / 'assets' / 'tiles'
OUTPUT = PROJECT_ROOT / 'data' / 'tiles' / 'tiles.json'

# 2) Extensiones que buscamos (recursivo)
EXTS = ('*.png', '*.PNG', '*.webp', '*.WEBP')


def main():
    mapping = {}
    # Recorrer todas subcarpetas y archivos
    for pattern in EXTS:
        for f in sorted(ASSETS_TILES.rglob(pattern)):
            # Ruta relativa desde assets/tiles, sin extensión
            rel = f.relative_to(ASSETS_TILES).with_suffix('')
            # Convertir a posix para uniformidad
            key = rel.as_posix()
            mapping[key] = rel.as_posix()

    # Escribir archivo JSON de overlay_map
    os.makedirs(OUTPUT.parent, exist_ok=True)
    with open(OUTPUT, 'w', encoding='utf-8') as out:
        json.dump(mapping, out, ensure_ascii=False, indent=2)
    print(f'[generate_overlay_map] Wrote overlay code map to {OUTPUT}')

if __name__ == '__main__':
    main()