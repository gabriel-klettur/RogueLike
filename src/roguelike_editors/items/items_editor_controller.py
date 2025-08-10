import pygame
import logging
from typing import Any, Dict, Optional

from .items_editor_models import ItemsEditorModel
from .items_editor_events import ItemsEditorEvents
from .items_editor_view import ItemsEditorView

from .items_picker_panel.items_picker_panel_controller import ItemPickerPanelController
from .items_properties_panel.items_properties_panel_controller import ItemsPropertiesPanelController
from .items_instances_panel.items_instances_panel_controller import ItemsInstancesPanelController
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile
import os
import uuid


class ItemsEditorController:
    """Orquestador del Editor de Ítems.

    Compone el PickerPanel y el PropertiesPanel y coordina su estado y eventos.
    """

    def __init__(self, items: Dict[str, Any], assets: Dict[str, Any], font: pygame.font.Font):
        # Modelo global
        self.model = ItemsEditorModel(items=items, assets=assets)
        # Subcontroladores
        self.picker_controller = ItemPickerPanelController(items, assets, font)
        self.properties_controller = ItemsPropertiesPanelController(items, font)
        self.instances_controller = ItemsInstancesPanelController(font)
        # Eventos/Vista del editor
        self.events = ItemsEditorEvents()
        self.view = ItemsEditorView()

        # Callbacks unificados de selección
        def _set_selected_item(item_id: str) -> None:
            # Actualizar SSOT del editor y del picker para resaltar en la grilla
            self.model.selected_item_id = item_id
            self.picker_controller.model.selected_item_id = item_id
            # Sincronizar inmediatamente el panel de propiedades para mostrar el ítem seleccionado
            self.properties_controller.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)

        def _on_open_id(item_id: str) -> None:
            # Seleccionar y abrir edición inline de la primera propiedad
            _set_selected_item(item_id)
            self.properties_controller.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)
            self.properties_controller.start_inline_edit()

        # Wire select from picker and from instances list
        self.picker_controller.on_select_id = _set_selected_item
        self.picker_controller.on_open_id = _on_open_id
        self.instances_controller.on_select_item_id = _set_selected_item

        # Delegar spawn RMB desde el picker hacia aquí
        def _spawn_at_player(item_id: str) -> None:
            if not hasattr(self, 'game') or not hasattr(self.game, 'ecs'):
                return
            pos = self.game.ecs.ecs_world.player_position
            if not pos:
                return
            inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
            drop_manager = ItemDropManager(inv_map_path)
            drop_id = uuid.uuid4().hex
            tile_x = int(pos.x) // TILE_SIZE
            tile_y = int(pos.y) // TILE_SIZE
            zone_id = get_zone_for_tile(tile_x, tile_y)
            drop_manager.create_drop(drop_id, item_id, 1, zone_id, position={'x': pos.x, 'y': pos.y})
            InventoryPickupSystem.recently_created.add(drop_id)
            # Refrescar la lista de instancias
            self.instances_controller.reload_data()

        self.picker_controller.on_spawn_at_player = _spawn_at_player

        # Visibilidad inicial sincronizada
        self.picker_controller.model.visible = self.model.visible

    # --- Ciclo principal ---
    def handle_event(self, event: pygame.event.Event) -> None:
        if not self.model.visible:
            return
        # Delegación centralizada
        self.events.handle_event(self, event)

    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
        # Asegurar visibilidad de subpaneles si la visibilidad global cambió externamente (F7)
        self.picker_controller.model.visible = True
        self.instances_controller.model.visible = True
        # Calcular rects de instancias para reservar altura exacta en el picker
        try:
            inst_list_rect, inst_params_rect = self.instances_controller.get_layout_rects()
        except Exception:
            inst_list_rect = inst_params_rect = None
        reserve_h = None
        if inst_list_rect and inst_params_rect:
            margin = self.instances_controller.model.margin
            reserve_h = inst_list_rect.h + inst_params_rect.h + 2 * margin
        else:
            # Fallback aproximado
            sw, sh = screen.get_size()
            margin = 20
            reserve_h = (sh // 4) + (sh // 4) + 2 * margin
        # Inyectar en la vista del picker para su layout
        setattr(self.picker_controller.view, '_reserved_bottom_h', reserve_h)
        # Primero, dibujar picker (incluye overlay y título)
        self.picker_controller.draw(screen)
        # Sincronizar hover tras render del picker
        self.model.hovered_item_id = self.picker_controller.model.hovered_item_id
        # Luego, dibujar propiedades con el rect del título del picker
        title_rect: Optional[pygame.Rect] = getattr(self.picker_controller.view, 'title_rect', None)
        self.properties_controller.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)
        self.properties_controller.draw(screen, title_rect)
        # Finalmente, panel de instancias del mapa + params
        list_rect, params_rect = inst_list_rect, inst_params_rect
        logging.getLogger(__name__).debug(f"[ItemsEditorController.draw] instances visible={self.instances_controller.model.visible} list_rect={list_rect} params_rect={params_rect} reserved_h={reserve_h}")
        self.instances_controller.draw(screen)

    # --- Visibilidad ---
    def show(self) -> None:
        self.model.visible = True
        self.picker_controller.model.visible = True
        self.instances_controller.model.visible = True
        # Refrescar datos de instancias al mostrar
        self.instances_controller.reload_data()

    def hide(self) -> None:
        self.model.visible = False
        self.picker_controller.model.visible = False
        self.instances_controller.model.visible = False

    def toggle(self) -> None:
        if self.model.visible:
            self.hide()
        else:
            self.show()

    # --- Integraciones auxiliares ---
    def set_game(self, game: Any) -> None:
        """Permite a features del picker (RMB spawn) acceder al juego."""
        self.picker_controller.game = game
        # El InstancesPanel no requiere acceso directo al juego

