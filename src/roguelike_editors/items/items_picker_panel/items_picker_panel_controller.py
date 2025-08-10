import pygame
# Initialize font module to ensure SysFont works in tests
pygame.font.init()
# Wrap SysFont to ensure font module is initialized on each call
_orig_sysfont = pygame.font.SysFont
import logging
logger = logging.getLogger(__name__)

def _safe_sysfont(*args, **kwargs):
    pygame.font.init()
    return _orig_sysfont(*args, **kwargs)

pygame.font.SysFont = _safe_sysfont
from typing import Any, Dict
from roguelike_editors.items.items_picker_panel.items_picker_panel_model import ItemPickerPanelModel
from roguelike_editors.items.items_picker_panel.items_picker_panel_view import ItemPickerPanelView

from roguelike_ui.widgets.map_items_ui import MapItemsUI
from roguelike_ui.widgets.params_editor_ui import ParamsEditorUI
from roguelike_ui.services.json_persistence import save_to_json
from roguelike_ui.widgets.picker_panel import PickerPanel, PickerPanelState
from roguelike_editors.items.items_properties_panel.items_properties_panel_controller import (
    ItemsPropertiesPanelController,
)
import os
import uuid
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_editors.items.items_picker_panel.items_picker_panel_events import ItemPickerPanelEventHandler
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile

class ItemPickerPanelController:
    """Controller para editor de ítems: maneja visibilidad y navegación."""
    def __init__(self, items: Dict[str, Any], assets: Dict[str, Any], font: pygame.font.Font):
        self.model = ItemPickerPanelModel(items=items, assets=assets)
        self.view = ItemPickerPanelView(assets, font)

        # Text input y double-click ahora son gestionados por el panel de propiedades
        # Inicializar servicios de edición de instancias
        # Map items list
        inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        self.map_ui = MapItemsUI(font, inv_map_path)
        # Manager para persistencia de drops
        self.drop_manager = ItemDropManager(inv_map_path)
        # Params editor
        schema_path = os.path.join(os.getcwd(), 'schemas', 'items', 'instances.json')
        self.params_ui = ParamsEditorUI(schema_path, font)
        # Enlazar al view
        self.view.map_ui = self.map_ui
        self.view.params_ui = self.params_ui

        # --- Properties Panel (separado del picker) ---
        self.properties_panel = ItemsPropertiesPanelController(items, font)

        # Reusable Picker Panel setup
        # State rect will be positioned each frame by the view
        self.picker_state = PickerPanelState(rect=pygame.Rect(0, 0, 0, 0))
        self.picker = PickerPanel(cell_size=(64, 64), draw_panel_bg=False, grid_bg_color=None, allow_dragging=False)

        def _get_item_ids() -> list[str]:
            # Excluir placeholder de imagen faltante y mantener orden estable
            return [i for i in self.model.items.keys() if i != "image_item_not_found"]

        self._get_item_ids = _get_item_ids  # store for reuse in callbacks

        self.picker.set_item_count(lambda: len(self._get_item_ids()))

        def _draw_item(surface: pygame.Surface, rect: pygame.Rect, index: int, selected: bool, hovered: bool) -> None:
            # Fondo de celda y icono escalado
            pygame.draw.rect(surface, (50, 50, 50), rect)
            item_ids = self._get_item_ids()
            if 0 <= index < len(item_ids):
                item_id = item_ids[index]
                icon = self.view.assets.get(item_id)
                if icon:
                    icon_surf = pygame.transform.smoothscale(icon, (rect.w, rect.h))
                    surface.blit(icon_surf, rect.topleft)

        self.picker.set_draw_item(_draw_item)

        def _on_select(index: int) -> None:
            item_ids = self._get_item_ids()
            if 0 <= index < len(item_ids):
                self.model.selected_item_id = item_ids[index]

        self.picker.on_select = _on_select
        # Abrir (doble clic) hace lo mismo que seleccionar por ahora
        self.picker.on_open = _on_select

        # Expose picker to the view for rendering and layout
        self.view.picker = self.picker
        self.view.picker_state = self.picker_state
        # Handler de eventos inline y grid
        self.event_handler = ItemPickerPanelEventHandler(self)

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
                logger.debug(f"[ItemEditorController][DEBUG] Computed tile coords ({tile_x},{tile_y}), zone '{zone_id}'")
                self.drop_manager.create_drop(drop_id, self.model.selected_item_id, 1, zone_id, position={'x': pos.x, 'y': pos.y})
                # Prevent immediate pickup of newly created drop
                InventoryPickupSystem.recently_created.add(drop_id)
                # Refrescar lista de instancias
                self.map_ui.load()
                logger.debug(f"[ItemEditorController] Agregado ítem {self.model.selected_item_id} con id {drop_id} en pos jugador ({pos.x},{pos.y}) zone='{zone_id}'")
            return

        # Delegar primero a panel de propiedades (captura edición de texto y clics en propiedades)
        if self.model.visible:
            # Vincular ids activos para correcto contexto
            self.properties_panel.set_active_ids(self.model.selected_item_id, self.model.hovered_item_id)
            self.properties_panel.set_items(self.model.items)
            self.properties_panel.handle_event(event)

        # Delegar entrada del picker (navegación/selección)
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
                        path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
                        save_to_json(path, inst_id, entry)
                        # refrescar lista de mapa
                        self.map_ui.load()
                    return
                except Exception as e:
                    logger.error(f"Params invalidos: {e}")
                    return
        return
    def draw(self, screen: pygame.Surface) -> None:
        # Mostrar editor de ítems original
        if not self.model.visible:
            return
        self.view.draw(screen, self.model)
        # Dibujar panel de propiedades a la derecha
        self.properties_panel.set_active_ids(self.model.selected_item_id, self.model.hovered_item_id)
        self.properties_panel.set_items(self.model.items)
        self.properties_panel.draw(screen, getattr(self.view, 'title_rect', None))
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
