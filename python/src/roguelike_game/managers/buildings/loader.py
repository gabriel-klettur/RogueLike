"""
Loader de edificios.
"""
from roguelike_editors.buildings.utils.load_buildings_from_json import load_buildings_from_json

class BuildingsLoader:
    """
    Carga edificios desde JSON.
    """
    def load(self, z_state):
        return load_buildings_from_json(z_state)
