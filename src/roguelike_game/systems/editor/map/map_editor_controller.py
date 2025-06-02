import json
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config import DATA_DIR
import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_engine.map.model.layer import Layer

class MapEditorController:
    """
    Lógica de negocio para el Map Editor.
    """
    def __init__(self, state, map_manager):
        self.state = state
        self.map_manager = map_manager
        # Toolbar for zone visibility
        self.toolbar = MapToolbarController(self.state)

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

    def rename_zone(self, old_name: str, new_name: str) -> None:
        """
        Renombra una zona: actualiza additional_zones, zone_offsets, map_manager y guarda cambios.
        """
        old_name = old_name.strip()
        new_name = new_name.strip()
        if not old_name or not new_name or old_name == new_name:
            return
        # Evitar conflicto con nombres existentes
        if new_name in global_map_settings.zone_offsets:
            return
        # Renombrar en additional_zones si existe
        parent_side = global_map_settings.additional_zones.pop(old_name, None)
        if parent_side is None:
            return
        global_map_settings.additional_zones[new_name] = parent_side
        # Limpiar cache de offsets
        global_map_settings.__dict__.pop('zone_offsets', None)
        # Actualizar map_manager.zone_rooms
        rooms = self.map_manager.zone_rooms.pop(old_name, [])
        self.map_manager.zone_rooms[new_name] = rooms
        # Actualizar map_manager.tiles_by_zone y tile.zone
        tiles = self.map_manager.tiles_by_zone.pop(old_name, [])
        for tile in tiles:
            tile.zone = new_name
        self.map_manager.tiles_by_zone[new_name] = tiles
        # Persistir cambios en zones.json
        self.save_zones()

    def save_zones(self):
        global_map_settings.use_zones_json = True
        path = DATA_DIR + '/zones/zones.json'
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(global_map_settings.zone_offsets, f, indent=2)

    def load_zones(self):
        global_map_settings.use_zones_json = True
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

# Toolbar component for Map Editor: single view_layers button
class MapToolbarController:
    """Toolbar for Map Editor: single view_layers button."""
    def __init__(self, editor_state):
        self.editor = editor_state
        ICON_PATH = "assets/ui/layers_view_tool.png"
        self.icon = load_image(ICON_PATH, (64, 64))
        self.x, self.y = 10, 10
        self.size = 64
        self.padding = 8
        self.icon_rect: pygame.Rect | None = None
        self.option_rects: dict[Layer, pygame.Rect] = {}

    def handle_click(self, mouse_pos) -> bool:
        # Toggle dropdown when clicking icon
        if self.icon_rect and self.icon_rect.collidepoint(mouse_pos):
            self.editor.layers_view_open = not self.editor.layers_view_open
            return True
        # Handle clicks on dropdown items
        if self.editor.layers_view_open:
            for key, rect in self.option_rects.items():
                if rect.collidepoint(mouse_pos):
                    if key == "show_all":
                        # Show all layers and buildings
                        for layer in self.editor.visible_layers:
                            self.editor.visible_layers[layer] = True
                        self.editor.show_buildings = True
                        print("[DEBUG][Layer View] show_all: all layers visible")
                    elif key == "hide_all":
                        # Hide all layers and buildings
                        for layer in self.editor.visible_layers:
                            self.editor.visible_layers[layer] = False
                        self.editor.show_buildings = False
                        print("[DEBUG][Layer View] hide_all: all layers hidden")
                    elif isinstance(key, Layer):
                        # Toggle tile layer visibility
                        self.editor.visible_layers[key] = not self.editor.visible_layers[key]
                        print(f"[DEBUG][Layer View] {key.name}: {'visible' if self.editor.visible_layers[key] else 'hidden'}")
                    elif key == "buildings":
                        # Toggle building layer visibility
                        self.editor.show_buildings = not self.editor.show_buildings
                        print(f"[DEBUG][Layer View] buildings: {'visible' if self.editor.show_buildings else 'hidden'}")
                    return True
        return False