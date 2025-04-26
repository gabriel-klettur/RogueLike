# src.roguelike_project/loaders/load_buildings.py

import os
from src.roguelike_game.entities.buildings.building import Building

from src.roguelike_game.systems.editor.buildings.model.persistence.json_handler import save_buildings_to_json
from src.roguelike_game.systems.editor.buildings.model.persistence.load_buildings_from_json import load_buildings_from_json
from src.roguelike_game.systems.z_layer.config import Z_LAYERS
from src.roguelike_engine.config import BUILDINGS_DATA_PATH

def get_hardcoded_buildings():
    return [
        Building(-1000, -2500, "assets/views/horizonte_1.png", solid=True, scale=(2048, 2048)),
        Building(2200, -800, "assets/buildings/temples/catholic.png", solid=True, scale=(1024, 1536)),
        Building(3000, -900, "assets/buildings/castles/castle_2.png", scale=(3072, 2048)),
        Building(500, -300, "assets/buildings/temples/satanist.png", solid=True, scale=(1024, 1024)),
        Building(1600, -100, "assets/buildings/others/portal.png", solid=True, scale=(512, 824)),
        Building(1950, 400, "assets/buildings/shops/alchemy_tower.png", solid=True, scale=(550, 845)),
        Building(3200, 900, "assets/buildings/shops/healer_1.png", solid=True, scale=(600, 600)),
        Building(4300, 966, "assets/buildings/shops/healer.png", solid=True, scale=(768, 768)),
        Building(1100, 466, "assets/buildings/others/fuente.png", solid=True, scale=(600, 600)),
        Building(600, 966, "assets/buildings/shops/blacksmith.png", solid=True, scale=(1200, 1200)),
        Building(2100, 1066, "assets/buildings/houses/orden_house_2.png", solid=True, scale=(1200, 1200)),
        Building(3600, 1266, "assets/buildings/shops/jewlery_shop.png", solid=True, scale=(1000, 1000)),
        Building(5000, 1766, "assets/buildings/others/guillotina.png", solid=True, scale=(400, 400))
    ]

def load_buildings(z_state=None):
    """
    Carga edificios desde JSON o fallback a los hardcodeados.
    Si se pasa `z_state`, se asignan las capas Z al cargarlos.
    """
    if os.path.exists(BUILDINGS_DATA_PATH):
        print("📦 Usando edificios desde JSON.")
        buildings = load_buildings_from_json(BUILDINGS_DATA_PATH, Building, z_state)        
        return buildings
    else:
        print("📦 Usando edificios hardcodeados (primera carga).")
        hardcoded = get_hardcoded_buildings()
        if z_state:            
            for b in hardcoded:
                z_state.set(b, Z_LAYERS["building_low"])
        save_buildings_to_json(hardcoded, BUILDINGS_DATA_PATH, z_state)
        return hardcoded
