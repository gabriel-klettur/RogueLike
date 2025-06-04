#!/usr/bin/env python3
"""
Script para migrar archivos de overlay de zonas al formato por capas.
Lee los overlays existentes (lista o dict) y los reescribe con la estructura:
{
  "layers": { "Ground": [...], ... }
}
"""
from pathlib import Path
from roguelike_engine.config.config import DATA_DIR
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.model.overlay.overlay_manager import load_layers, save_layers


def main():
    # Procesar cada zona y la zona "no_zone"
    zones = list(global_map_settings.zone_offsets.keys()) + ["no_zone"]
    for zone in zones:
        layers = load_layers(zone)
        if not layers:
            print(f"⚠️ No se encontró overlay para zona '{zone}', se omite.")
            continue
        save_layers(zone, layers)
        print(f"✅ Overlay de zona '{zone}' migrado a formato por capas.")


if __name__ == '__main__':
    main()
