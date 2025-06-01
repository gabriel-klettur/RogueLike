import json
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config import DATA_DIR

class MapEditorController:
    """
    Lógica de negocio para el Map Editor.
    """
    def __init__(self, state, map_manager):
        self.state = state
        self.map_manager = map_manager

    def select_zone(self, zone_name):
        if zone_name in self.map_manager.tiles_by_zone:
            self.state.selected_zone = zone_name

    def toggle_hide_zone(self, zone_name):
        if zone_name in self.state.hidden_zones:
            self.state.hidden_zones.remove(zone_name)
        else:
            self.state.hidden_zones.add(zone_name)

    def move_zone(self, zone_name, dx, dy):
        offs = global_map_settings.zone_offsets
        x, y = offs[zone_name]
        offs[zone_name] = (x+dx, y+dy)

    def duplicate_zone(self):
        sel = self.state.selected_zone
        if not sel:
            return
        offs = global_map_settings.zone_offsets
        new_key = sel + "_copy"
        offs[new_key] = offs[sel]
        self.map_manager.zone_rooms[new_key] = list(self.map_manager.zone_rooms.get(sel, []))
        self.map_manager.matrix = self.map_manager.matrix[:]  # placeholder

    def delete_zone(self):
        sel = self.state.selected_zone
        if not sel or sel == 'lobby':
            return
        # eliminar de offsets y matrix
        global_map_settings.additional_zones.pop(sel, None)
        global_map_settings.__dict__.pop('zone_offsets', None)
        self.map_manager.expand_zone('', '', '')  # refrescar offsets

    def save_zones(self):
        path = DATA_DIR + '/zones/zones.json'
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(global_map_settings.zone_offsets, f, indent=2)

    def load_zones(self):
        path = DATA_DIR + '/zones/zones.json'
        try:
            with open(path, 'r', encoding='utf-8') as f:
                data = json.load(f)
            global_map_settings.additional_zones.clear()
            for k,(x,y) in data.items():
                global_map_settings.additional_zones[k] = (None, None)
            global_map_settings.__dict__.pop('zone_offsets', None)
        except Exception:
            pass