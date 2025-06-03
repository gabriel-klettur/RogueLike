import json
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config import DATA_DIR
import pygame
import os
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

    def add_zone(self, tx: int, ty: int) -> None:
        """Agrega una nueva zona 50x50 alineada al grid de zonas."""
        zone_w, zone_h = global_map_settings.zone_size
        offx = (tx // zone_w) * zone_w
        offy = (ty // zone_h) * zone_h
        new_name = f"zone_{offx}_{offy}"
        path = DATA_DIR + '/zones/zones.json'
        try:
            with open(path, 'r', encoding='utf-8') as f:
                offsets = json.load(f)
        except Exception:
            offsets = {}
        # Nombre único
        base = new_name; idx = 1
        while new_name in offsets:
            new_name = f"{base}_{idx}"; idx += 1
        offsets[new_name] = [offx, offy]
        # Persistir JSON
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(offsets, f, indent=2)
        # Recargar offsets y mapa
        global_map_settings.__dict__.pop('zone_offsets', None)
        self.map_manager.reload_map()
        self.state.selected_zone = new_name
        print(f"DEBUG [Controller.add_zone] Added zone {new_name} at offset {(offx, offy)}")

    def delete_zone(self):
        sel = self.state.selected_zone
        if not sel or sel == 'lobby':
            return
        path = DATA_DIR + '/zones/zones.json'
        try:
            with open(path, 'r', encoding='utf-8') as f:
                offsets = json.load(f)
        except Exception:
            offsets = {}
        offsets.pop(sel, None)
        # Persistir JSON
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(offsets, f, indent=2)
        # Delete collision file for this zone
        coll_path = os.path.join(DATA_DIR, 'collisions', f'{sel}.json')
        if os.path.isfile(coll_path):
            try:
                os.remove(coll_path)
                print(f"DEBUG [Controller.delete_zone] Removed collision file {coll_path}")
            except Exception as e:
                print(f"DEBUG [Controller.delete_zone] failed to remove collision file {coll_path}: {e}")
        # Delete overlay file for this zone
        overlay_path = os.path.join(DATA_DIR, 'zones', 'overlays', f'{sel}.overlay.json')
        if os.path.isfile(overlay_path):
            try:
                os.remove(overlay_path)
                print(f"DEBUG [Controller.delete_zone] Removed overlay file {overlay_path}")
            except Exception as e:
                print(f"DEBUG [Controller.delete_zone] failed to remove overlay file {overlay_path}: {e}")
        # Recargar offsets y mapa
        global_map_settings.__dict__.pop('zone_offsets', None)
        self.map_manager.reload_map()
        self.state.selected_zone = None
        print(f"DEBUG [Controller.delete_zone] Removed zone {sel}")

    def rename_zone(self, old_name: str, new_name: str) -> None:
        """
        Renombra una zona: actualiza JSON zone_offsets mapping directamente, soporta renombrar cualquier zona y elimina lógica de additional_zones.
        """
        old_name = old_name.strip()
        new_name = new_name.strip()
        print(f"DEBUG [Controller.rename_zone] called with old_name={old_name!r}, new_name={new_name!r}")
        if not old_name or not new_name or old_name == new_name:
            print("DEBUG [Controller.rename_zone] abort: invalid or same name")
            return
        # Refactor: edit JSON mapping of offsets to rename any zone
        global_map_settings.use_zones_json = True
        offsets = dict(global_map_settings.zone_offsets)
        print(f"DEBUG [Controller.rename_zone] loaded offsets before rename: {offsets}")
        if old_name not in offsets or new_name in offsets:
            print("DEBUG [Controller.rename_zone] abort: old_name not in offsets or new_name exists")
            return
        # Update mapping and persist
        offsets[new_name] = offsets.pop(old_name)
        print(f"DEBUG [Controller.rename_zone] offsets after rename: {offsets}")
        path = DATA_DIR + '/zones/zones.json'
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(offsets, f, indent=2)
        print(f"DEBUG [Controller.rename_zone] saved zones.json at {path}")
        # Clear cached offsets to force reload
        global_map_settings.__dict__.pop('zone_offsets', None)
        print("DEBUG [Controller.rename_zone] cleared cached zone_offsets")
        # Actualizar map_manager.zone_rooms
        rooms = self.map_manager.zone_rooms.pop(old_name, [])
        print(f"DEBUG [Controller.rename_zone] updated zone_rooms keys: {list(self.map_manager.zone_rooms.keys())}")
        self.map_manager.zone_rooms[new_name] = rooms
        # Actualizar map_manager.tiles_by_zone y tile.zone
        tiles = self.map_manager.tiles_by_zone.pop(old_name, [])
        print(f"DEBUG [Controller.rename_zone] updated tiles_by_zone keys before: {list(self.map_manager.tiles_by_zone.keys())}")
        for tile in tiles:
            tile.zone = new_name
        self.map_manager.tiles_by_zone[new_name] = tiles
        print(f"DEBUG [Controller.rename_zone] updated tiles_by_zone keys after: {list(self.map_manager.tiles_by_zone.keys())}")

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
        # Add Zone and Delete Zone icons
        self.add_icon = load_image("assets/ui/add_zone.png", (64, 64))
        self.delete_icon = load_image("assets/ui/delete_zone.png", (64, 64))
        # Clear/Paint Tiles and Colliders icons
        self.paint_tiles_icon = load_image("assets/ui/pintar_tiles_zone.png", (64, 64))
        self.clear_colliders_icon = load_image("assets/ui/vaciar_colliders_zone.png", (64, 64))
        self.paint_colliders_icon = load_image("assets/ui/pintar_colliders_zone.png", (64, 64))
        self.x, self.y = 10, 10
        self.size = 64
        self.padding = 8
        # Icon rects for each tool
        self.icon_rect: pygame.Rect | None = None
        self.add_rect: pygame.Rect | None = None
        self.delete_rect: pygame.Rect | None = None
        self.paint_tiles_rect: pygame.Rect | None = None
        self.clear_colliders_rect: pygame.Rect | None = None
        self.paint_colliders_rect: pygame.Rect | None = None
        self.option_rects: dict[Layer, pygame.Rect] = {}

    def handle_click(self, mouse_pos) -> bool:
        # Toggle dropdown when clicking icon
        if self.icon_rect and self.icon_rect.collidepoint(mouse_pos):
            self.editor.layers_view_open = not self.editor.layers_view_open
            return True
        # Handle Add Zone button click
        if self.add_rect and self.add_rect.collidepoint(mouse_pos):
            # Toggle add zone mode, disable delete mode
            self.editor.add_zone_mode = not self.editor.add_zone_mode
            self.editor.delete_zone_mode = False
            print(f"[DEBUG][Toolbar] add_zone_mode set to {self.editor.add_zone_mode}")
            return True
        # Handle Delete Zone button click
        if self.delete_rect and self.delete_rect.collidepoint(mouse_pos):
            # Toggle delete zone mode, disable add mode
            self.editor.delete_zone_mode = not self.editor.delete_zone_mode
            self.editor.add_zone_mode = False
            print(f"[DEBUG][Toolbar] delete_zone_mode set to {self.editor.delete_zone_mode}")
            return True
        # Handle Paint Tiles Zone button click
        if self.paint_tiles_rect and self.paint_tiles_rect.collidepoint(mouse_pos):
            print("[DEBUG][Toolbar] clicked Paint Tiles Zone button")
            self.editor.paint_tiles_mode = not self.editor.paint_tiles_mode
            self.editor.add_zone_mode = False
            self.editor.delete_zone_mode = False
            self.editor.clear_colliders_mode = False
            self.editor.paint_colliders_mode = False
            print(f"[DEBUG][Toolbar] paint_tiles_mode set to {self.editor.paint_tiles_mode}")
            return True
        # Handle Clear Colliders Zone button click
        if self.clear_colliders_rect and self.clear_colliders_rect.collidepoint(mouse_pos):
            print("[DEBUG][Toolbar] clicked Clear Colliders Zone button")
            self.editor.clear_colliders_mode = not self.editor.clear_colliders_mode
            self.editor.paint_tiles_mode = False
            self.editor.add_zone_mode = False
            self.editor.delete_zone_mode = False
            self.editor.paint_colliders_mode = False
            print(f"[DEBUG][Toolbar] clear_colliders_mode set to {self.editor.clear_colliders_mode}")
            return True
        # Handle Paint Colliders Zone button click
        if self.paint_colliders_rect and self.paint_colliders_rect.collidepoint(mouse_pos):
            print("[DEBUG][Toolbar] clicked Paint Colliders Zone button")
            self.editor.paint_colliders_mode = not self.editor.paint_colliders_mode
            self.editor.paint_tiles_mode = False
            self.editor.add_zone_mode = False
            self.editor.delete_zone_mode = False
            self.editor.clear_colliders_mode = False
            print(f"[DEBUG][Toolbar] paint_colliders_mode set to {self.editor.paint_colliders_mode}")
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