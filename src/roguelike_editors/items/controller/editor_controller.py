import pygame
# Initialize font module to ensure SysFont works in tests
pygame.font.init()
# Wrap SysFont to ensure font module is initialized on each call
_orig_sysfont = pygame.font.SysFont

def _safe_sysfont(*args, **kwargs):
    pygame.font.init()
    return _orig_sysfont(*args, **kwargs)

pygame.font.SysFont = _safe_sysfont
from typing import Any, Dict
from roguelike_editors.items.model.editor_model import ItemEditorModel
from roguelike_editors.items.view.editor_view import ItemEditorView

from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_ui.widgets.map_items_ui import MapItemsUI
from roguelike_ui.widgets.params_editor_ui import ParamsEditorUI
from roguelike_ui.services.json_persistence import save_to_json
import os
import uuid
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_editors.items.events.items_editor_events import ItemsEditorEventHandler
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile

class ItemEditorController:
    """Controller para editor de ítems: maneja visibilidad y navegación."""
    def __init__(self, items: Dict[str, Any], assets: Dict[str, Any], font: pygame.font.Font):
        self.model = ItemEditorModel(items=items, assets=assets)
        self.view = ItemEditorView(assets, font)

        # Initialize text input and double-click detector
        self.text_input = TextInput(font)
        self.dc_detector = DoubleClickDetector()
        self.view.text_input = self.text_input
        # Inicializar servicios de edición de instancias
        # Map items list
        inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_map.json')
        self.map_ui = MapItemsUI(font, inv_map_path)
        # Manager para persistencia de drops
        self.drop_manager = ItemDropManager(inv_map_path)
        # Params editor
        schema_path = os.path.join(os.getcwd(), 'schemas', 'items', 'instances.json')
        self.params_ui = ParamsEditorUI(schema_path, font)
        # Enlazar al view
        self.view.map_ui = self.map_ui
        self.view.params_ui = self.params_ui
        # Handler de eventos inline y grid
        self.event_handler = ItemsEditorEventHandler(self)

    def handle_event(self, event: pygame.event.Event) -> None:
        # Añadir nuevo ítem al mapa con clic derecho (pies del jugador)
        if self.model.visible and event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            if self.model.selected_item_id and hasattr(self, 'game') and hasattr(self.game, 'ecs'):
                pos = self.game.ecs.ecs_world.player_position
                if not pos:
                    return
                drop_id = uuid.uuid4().hex
                # Agregar con cantidad por defecto 1 y zona nula
                # Registrar drop en la posición y zona del jugador
                tile_x = int(pos.x) // TILE_SIZE
                tile_y = int(pos.y) // TILE_SIZE
                zone_id = get_zone_for_tile(tile_x, tile_y)
                print(f"[ItemEditorController][DEBUG] Computed tile coords ({tile_x},{tile_y}), zone '{zone_id}'")
                self.drop_manager.create_drop(drop_id, self.model.selected_item_id, 1, zone_id, position={'x': pos.x, 'y': pos.y})
                # Prevent immediate pickup of newly created drop
                InventoryPickupSystem.recently_created.add(drop_id)
                # Refrescar lista de instancias
                self.map_ui.load()
                print(f"[ItemEditorController] Agregado ítem {self.model.selected_item_id} con id {drop_id} en pos jugador ({pos.x},{pos.y}) zone='{zone_id}'")
            return

        # Delegar entrada inline (grid, detalles, edición) al handler existente
        self.event_handler.handle(event)
        # Integración de lista de instancias del mapa y edición de params
        if self.model.visible:
            # Selección de instancia en mapa
            inst = self.map_ui.handle_event(event)
            if inst:
                inst_data = self.map_ui.data.get(inst, {})
                # Seleccionar ítem en el grid de definiciones
                item_def = inst_data.get('item_id')
                if item_def:
                    self.model.selected_item_id = item_def
                # cargar valores al editor de params
                params = inst_data.get('params', {})
                self.params_ui.load_values(params)
                return
            # Manejo de edición de params
            if self.params_ui.handle_event(event):
                try:
                    new_params = self.params_ui.get_values()
                    inst_id = self.map_ui.selected_instance
                    if inst_id:
                        # actualizar datos en memoria y persistir
                        entry = self.map_ui.data.get(inst_id, {})
                        entry['params'] = new_params
                        path = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_map.json')
                        save_to_json(path, inst_id, entry)
                        # refrescar lista de mapa
                        self.map_ui.load()
                    return
                except ValidationError as e:
                    print(f"Params invalidos: {e}")
                    return
        self.event_handler.handle(event)
        return
        

    def _commit_edit(self):
        if not self.model.editing_property:
            return
        item_id = self.model.selected_item_id or self.model.hovered_item_id
        if item_id and item_id in self.model.items:
            item = self.model.items[item_id]
            key = self.model.editing_property
            new_text = self.model.editing_text
            old_val = getattr(item, key, None)
            try:
                if isinstance(old_val, bool):
                    converted = new_text.lower() in ("true", "1", "yes")
                elif isinstance(old_val, int):
                    converted = int(new_text)
                elif isinstance(old_val, float):
                    converted = float(new_text)
                else:
                    converted = new_text
            except ValueError:
                converted = new_text
            try:
                setattr(item, key, converted)
            except Exception as e:
                print(f"[ItemEditor] Invalid assignment for {key}: '{converted}', error: {e}")
                # cleanup on invalid input
                self.text_input.deactivate()
                self.model.editing_property = None
                self.model.editing_text = ""
                self.model.editing_cursor = 0
                return
            # Guardar JSON
            from roguelike_ui.services.json_persistence import load_from_json
            path = os.path.join(os.getcwd(), "data", "items", "items.json")
            data = load_from_json(path)
            entry = data.get(item_id, {})
            entry[key] = converted
            save_to_json(path, item_id, entry)
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0
    def draw(self, screen: pygame.Surface) -> None:
        # Mostrar editor de ítems original
        if not self.model.visible:
            return
        self.view.draw(screen, self.model)
        # Overlay de edición inline
        if self.model.editing_property:
            for rect_prop, key_prop in self.model.property_entries:
                if key_prop == self.model.editing_property:
                    prefix = f"{key_prop}: "
                    x = rect_prop.x + self.view.font.size(prefix)[0]
                    y = rect_prop.y
                    self.text_input.draw(screen, x, y)
                    break
        # Añadir lista de instancias del mapa y editor de params debajo
        margin = 20
        sw, sh = screen.get_size()
        # Panel de params en la parte inferior
        params_h = sh // 4
        list_h = sh // 4
        params_rect = pygame.Rect(margin, sh - margin - params_h, sw - 2*margin, params_h)
        # Dibujar params si hay instancia seleccionada
        if getattr(self.map_ui, 'selected_instance', None):
            self.params_ui.draw(screen, params_rect)
        # Panel de lista de instancias justo encima
        list_rect = pygame.Rect(margin, params_rect.y - margin - list_h, sw - 2*margin, list_h)
        self.map_ui.draw(screen, list_rect)
