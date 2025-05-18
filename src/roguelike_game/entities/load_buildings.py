# Path: src/roguelike_game/entities/load_buildings.py
from roguelike_game.entities.buildings.building import Building
from roguelike_game.systems.editor.buildings.model.persistence.load_buildings_from_json import load_buildings_from_json
from roguelike_engine.config import BUILDINGS_DATA_PATH

def load_buildings(z_state=None):
    """
    Carga edificios desde JSON.
    Si se pasa `z_state`, se asignan las capas Z al cargarlos.
    """
        
    buildings = load_buildings_from_json(BUILDINGS_DATA_PATH, Building, z_state)        
    return buildings

    