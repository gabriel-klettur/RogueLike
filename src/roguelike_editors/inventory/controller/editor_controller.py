import pygame
import os
import json
from roguelike_ui.services.json_persistence import load_from_json, save_to_json

from roguelike_editors.inventory.model.editor_model import InventoryEditorModel
from roguelike_editors.inventory.view.editor_view import InventoryEditorView
from roguelike_editors.inventory.events.inventory_editor_events import InventoryEditorEventHandler

class InventoryEditorController:
    """
    Controller para el editor de inventario (MVC): maneja estados y eventos.
    """
    def __init__(self, world, assets: dict, font: pygame.font.Font):
        self.model = InventoryEditorModel()
        self.world = world
        self.assets = assets
        self.font = font
        self.view = InventoryEditorView(assets, font)
        self.event_handler = InventoryEditorEventHandler(self)
        # Paths por categoría
        cwd = os.getcwd()
        self.paths = {
            'player': {'default': os.path.join(cwd, 'data', 'defaults', 'inventory_player.json'), 'active': os.path.join(cwd, 'data', 'inventory', 'inventory_player.json')},
            'monsters': {'default': os.path.join(cwd, 'data', 'defaults', 'inventory_monsters.json'), 'active': os.path.join(cwd, 'data', 'inventory', 'inventory_monsters.json')},
            'map': {'default': os.path.join(cwd, 'data', 'defaults', 'inventory_map.json'), 'active': os.path.join(cwd, 'data', 'inventory', 'inventory_map.json')}
        }
        # Cargar datos JSON en el modelo
        for cat, p in self.paths.items():
            self.model.default_data[cat] = load_from_json(p['default'])
            self.model.active_data[cat] = load_from_json(p['active'])

    def _save_default(self):
        cat = self.model.current_category
        path = self.paths[cat]['default']
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(self.model.default_data.get(cat, {}), f, indent=2)

    def _save_active(self):
        cat = self.model.current_category
        path = self.paths[cat]['active']
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(self.model.active_data.get(cat, {}), f, indent=2)

    def handle_event(self, event):
        # Delega a ItemEditorEventHandler
        self.event_handler.handle(event)



    def draw(self, screen):
        self.view.draw(screen, self.model, self.world)

    def _save_template(self, inv):
        # Guarda plantilla en defaults
        data = {}
        path = self.default_player_path if self.model.selected_eid in self.world.components.get('PlayerTagComponent', {}) else self.default_monster_path
        try:
            with open(path, 'r', encoding='utf-8') as f:
                data = json.load(f)
        except:
            pass
        out = {
            'player_id': getattr(inv, 'player_id', None),
            'capacity': getattr(inv, 'capacity', None),
            'slots': inv.serialize().get('slots'),
            'schema_version': data.get('schema_version', '1.0.0')
        }
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(out, f, indent=2)

    def _apply_changes(self, inv):
        # Aplica cambios a JSON activos
        path = self.active_player_path if self.model.selected_eid in self.world.components.get('PlayerTagComponent', {}) else self.active_monster_path
        try:
            with open(path, 'r', encoding='utf-8') as f:
                d = json.load(f)
        except:
            d = {}
        key = None
        for eid_str in d:
            if int(eid_str) == self.model.selected_eid:
                key = eid_str
                break
        if key is None:
            key = str(self.model.selected_eid)
        d[key] = {
            'player_id': getattr(inv, 'player_id', None),
            'slots': inv.serialize().get('slots'),
            'schema_version': d.get(key, {}).get('schema_version', '1.0.0')
        }
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(d, f, indent=2)
