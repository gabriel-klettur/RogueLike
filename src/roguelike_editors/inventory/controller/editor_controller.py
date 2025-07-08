import pygame
import os
import json
from roguelike_ui.services.json_persistence import load_from_json, save_to_json

from roguelike_editors.inventory.model.editor_model import InventoryEditorModel
from roguelike_editors.inventory.view.editor_view import InventoryEditorView
from roguelike_game.ecs.components.item_models import ItemStack
from types import SimpleNamespace

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
        # Paths por categoría
        cwd = os.getcwd()
        self.paths = {
            'player': {'default': os.path.join(cwd, 'data', 'defaults', 'inventory_player.json'), 'active': os.path.join(cwd, 'data', 'inventory_player.json')},
            'monsters': {'default': os.path.join(cwd, 'data', 'defaults', 'inventory_monsters.json'), 'active': os.path.join(cwd, 'data', 'inventory_monsters.json')},
            'map': {'default': os.path.join(cwd, 'data', 'defaults', 'inventory_map.json'), 'active': os.path.join(cwd, 'data', 'inventory_map.json')}
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
        # Debug F7
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F7:
            print("[DEBUG InventoryEditorController] F7 pressed")
        # Toggle editor con F6
        if event.type == pygame.KEYDOWN and event.key == pygame.K_F6:
            self.model.visible = not self.model.visible
            print(f"[DEBUG InventoryEditorController] F6 pressed, visible={self.model.visible}")
            if self.model.visible:
                # Inicializar lista de entidades
                players = list(self.world.components.get('PlayerTagComponent', {}).keys())
                npcs = list(self.world.components.get('NPCTagComponent', {}).keys())
                self.model.entities = players + npcs
                self.model.selected_eid = self.model.entities[0] if self.model.entities else None
            return
        if not self.model.visible:
            return
        # Vertical scroll con rueda del ratón
        if event.type == pygame.MOUSEBUTTONDOWN:
            if event.button == 4:  # rueda arriba
                self.model.scroll_offset -= self.font.get_linesize()
                return
            elif event.button == 5:  # rueda abajo
                self.model.scroll_offset += self.font.get_linesize()
                return
        # Gestión de pestañas y guardado JSON
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            # Cambiar categoría
            for rect, cat in getattr(self.view, 'tab_rects', []):
                if rect.collidepoint(mx, my):
                    self.model.current_category = cat
                    return
            # Guardar default

            # Guardar active

        # Selección de entidad con flechas
        if event.type == pygame.KEYDOWN:
            # Atajo de teclado para cambiar categoría (1: Player, 2: Monsters, 3: Map)
            if event.key == pygame.K_1:
                self.model.current_category = 'player'
                return
            if event.key == pygame.K_2:
                self.model.current_category = 'monsters'
                return
            if event.key == pygame.K_3:
                self.model.current_category = 'map'
                return
            if event.key == pygame.K_LEFT and not self.model.prev_left:
                idx = self.model.entities.index(self.model.selected_eid)
                self.model.selected_eid = self.model.entities[(idx - 1) % len(self.model.entities)]
            if event.key == pygame.K_RIGHT and not self.model.prev_right:
                idx = self.model.entities.index(self.model.selected_eid)
                self.model.selected_eid = self.model.entities[(idx + 1) % len(self.model.entities)]
            self.model.prev_left = (event.key == pygame.K_LEFT)
            self.model.prev_right = (event.key == pygame.K_RIGHT)
        # Mouse down
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            inv = self.world.components.get('InventoryComponent', {}).get(self.model.selected_eid)
            if inv and self.model.drag_item is None:
                slot_idx = self.view.get_slot_at_pos((mx, my), len(inv.slots))
                if slot_idx is not None and inv.slots[slot_idx]:
                    stack = inv.slots[slot_idx]
                    self.model.drag_item = stack
                    self.model.drag_slot = slot_idx
                    inv.slots[slot_idx] = None
        # Mouse up
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            inv = self.world.components.get('InventoryComponent', {}).get(self.model.selected_eid)
            if inv and self.model.drag_item is not None:
                slot_idx = self.view.get_slot_at_pos((mx, my), len(inv.slots))
                if slot_idx is not None and inv.slots[slot_idx] is None:
                    inv.slots[slot_idx] = self.model.drag_item
                else:
                    inv.slots[self.model.drag_slot] = self.model.drag_item
                self.model.drag_item = None
                self.model.drag_slot = None
            # Botones
            if self.view.save_default_rect and self.view.save_default_rect.collidepoint(mx, my):
                self._save_default()
                return
            if self.view.save_active_rect and self.view.save_active_rect.collidepoint(mx, my):
                self._save_active()
                return



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
