#!/usr/bin/env python3
import json
from pathlib import Path

from roguelike_engine.config.config import DATA_DIR
from roguelike_engine.config.map_config import global_map_settings

def main():
    # Ruta al overlay global actual
    global_path = Path(DATA_DIR) / "map_overlays" / "global_map.overlay.json"
    if not global_path.is_file():
        print(f"⚠️ No se encontró overlay global: {global_path}")
        return

    # Cargar matriz completa
    overlay = json.loads(global_path.read_text(encoding="utf-8"))

    # Directorio donde guardaremos por-zona
    zones_dir = Path(DATA_DIR) / "zones" / "overlays"
    zones_dir.mkdir(parents=True, exist_ok=True)

    w = global_map_settings.zone_width
    h = global_map_settings.zone_height

    # Para cada zona, recortamos la submatriz y la guardamos
    for zone, (ox, oy) in global_map_settings.zone_offsets.items():
        sub = [ row[ox:ox + w] for row in overlay[oy:oy + h] ]
        out = zones_dir / f"{zone}.overlay.json"
        out.write_text(json.dumps(sub, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"✅ Overlay de zona '{zone}' escrito en {out}")

if __name__ == "__main__":
    main()
