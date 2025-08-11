import pygame
import logging
from typing import Any, Dict, Optional
from types import SimpleNamespace

from .items_editor_models import ItemsEditorModel
from .items_editor_events import ItemsEditorEvents
from .items_editor_view import ItemsEditorView
from .items_tool_bar_panel.items_tool_bar_panel_model import ItemsToolBarPanelModel
from .items_tool_bar_panel.items_tool_bar_panel_view import ItemsToolBarPanelView
from .items_tool_bar_panel.items_tool_bar_panel_events import ItemsToolBarPanelEventHandler
from .items_tool_bar_panel.items_tool_bar_panel_controller import ItemsToolBarPanelController
from .items_add_remove_panel.items_add_remove_panel_model import ItemsAddRemovePanelModel
from .items_add_remove_panel.items_add_remove_panel_view import ItemsAddRemovePanelView
from .items_add_remove_panel.items_add_remove_panel_events import ItemsAddRemovePanelEventHandler
from .items_add_remove_panel.items_add_remove_panel_controller import ItemsAddRemovePanelController

from .items_picker_panel.items_picker_panel_controller import ItemPickerPanelController
from .items_title_panel.items_title_view import ItemsTitleView
from .items_properties_panel.items_properties_panel_controller import ItemsPropertiesPanelController
from .items_instances_panel.items_instances_panel_controller import ItemsInstancesPanelController
from roguelike_game.managers.map.item_drop_manager import ItemDropManager
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.config_z_layer import DEFAULT_Z
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.item_models import load_items
from roguelike_game.ecs.systems.inventory.map_load_drops_system import MapLoadDropsSystem
from roguelike_game.ecs.systems.rendering.drop_hover_system import DropHoverRenderSystem
import os
import uuid
from roguelike_engine.utils.loader import load_image


class ItemsEditorController:
    """Orquestador del Editor de Ítems.

    Compone el PickerPanel y el PropertiesPanel y coordina su estado y eventos.
    """

    def __init__(self, items: Dict[str, Any], assets: Dict[str, Any], font: pygame.font.Font):
        # Modelo global
        self.model = ItemsEditorModel(items=items, assets=assets)
        # Fuente para overlays y textos auxiliares
        self.font = font
        # Subcontroladores
        self.picker_controller = ItemPickerPanelController(items, assets, font)
        self.properties_controller = ItemsPropertiesPanelController(items, font)
        self.instances_controller = ItemsInstancesPanelController(font)
        # Eventos/Vista del editor
        self.events = ItemsEditorEvents()
        self.view = ItemsEditorView()

        # Título propio del Items Editor: siempre visible cuando el editor está abierto
        class _ItemsTitleController:
            def __init__(self, state_model):
                self.view = ItemsTitleView(self, state_model)
            def render(self, screen: pygame.Surface) -> pygame.Rect:
                return self.view.render(screen)
        self.title_controller = _ItemsTitleController(self.model)

        # Debug snapshots to avoid log spam
        self._last_inst_list_rect: pygame.Rect | None = None
        self._last_inst_params_rect: pygame.Rect | None = None
        self._last_reserved_h: int | None = None

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

        # Press-and-hold focus from instances list
        def _start_hold_focus(x: float, y: float) -> None:
            if not hasattr(self, 'game'):
                return
            try:
                self.game.camera.update(SimpleNamespace(x=x, y=y))
                self.model.holding_pos_focus = True
            except Exception:
                logging.getLogger(__name__).exception("[ItemsEditorController] start_hold_focus failed")

        def _end_hold_focus() -> None:
            if not hasattr(self, 'game'):
                self.model.holding_pos_focus = False
                return
            try:
                pos = getattr(self.game.ecs.ecs_world, 'player_position', None)
                if pos is not None:
                    self.game.camera.update(SimpleNamespace(x=pos.x, y=pos.y))
                self.model.holding_pos_focus = False
            except Exception:
                logging.getLogger(__name__).exception("[ItemsEditorController] end_hold_focus failed")

        self.instances_controller.on_start_hold_focus = _start_hold_focus
        self.instances_controller.on_end_hold_focus = _end_hold_focus

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

        # Visibilidad inicial: el Picker inicia oculto; se muestra con el botón 'items_on_map'
        self.picker_controller.model.visible = False

        # --- Toolbars de Items (viven dentro del Items Editor) ---
        # Toolbar principal
        self.items_toolbar_model = ItemsToolBarPanelModel()
        self.items_toolbar_view = ItemsToolBarPanelView(self, self.items_toolbar_model)
        self.items_toolbar_event_handler = ItemsToolBarPanelEventHandler(self, self.items_toolbar_model)
        self.items_toolbar_controller = ItemsToolBarPanelController(
            self, self.items_toolbar_model, self.items_toolbar_view, self.items_toolbar_event_handler
        )
        # Sub-toolbar Add/Remove (inicialmente oculto)
        self.items_add_remove_model = ItemsAddRemovePanelModel()
        self.items_add_remove_view = ItemsAddRemovePanelView(self, self.items_add_remove_model)
        self.items_add_remove_event_handler = ItemsAddRemovePanelEventHandler(self, self.items_add_remove_model)
        self.items_add_remove_controller = ItemsAddRemovePanelController(
            self, self.items_add_remove_model, self.items_add_remove_view, self.items_add_remove_event_handler
        )
        # Vincular referencia cruzada si el controlador la usa
        try:
            self.items_toolbar_controller.add_remove_controller = self.items_add_remove_controller
        except Exception:
            pass

        # --- Wire assets picker anchor and change notifications ---
        def _assets_anchor_rect() -> Optional[pygame.Rect]:
            try:
                # Use picker panel rect computed by ItemPickerPanelView
                rect = getattr(self.picker_controller, 'picker_state', None)
                if rect is not None and getattr(rect, 'rect', None):
                    return rect.rect
            except Exception:
                pass
            return None

        def _on_asset_changed(item_id: str, new_asset_path: str) -> None:
            try:
                # Load new image and update shared assets dict
                img = load_image(new_asset_path)
                # Update editor-level assets
                self.model.assets[item_id] = img
                # Keep picker model/view in sync (they likely share the same dict, but ensure)
                try:
                    self.picker_controller.model.assets[item_id] = img
                except Exception:
                    pass
                try:
                    # Some views cache the assets ref; replace the entry
                    self.picker_controller.view.assets[item_id] = img
                except Exception:
                    pass
            except Exception:
                logging.getLogger(__name__).exception("[ItemsEditorController] Failed to refresh asset image for '%s'", item_id)

        self.properties_controller.get_assets_anchor_rect = _assets_anchor_rect
        self.properties_controller.on_asset_changed = _on_asset_changed

        # Hover rendering system (reuse in editor to match in-game behavior)
        try:
            self._hover_renderer = DropHoverRenderSystem(perf_log=None)
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] Failed to init DropHoverRenderSystem")

    # --- Ciclo principal ---
    def handle_event(self, event: pygame.event.Event) -> None:
        # Delegación centralizada (ItemsEditorEvents maneja F7/ESC siempre)
        handled = self.events.handle_event(self, event)
        if handled:
            return
        # Si visible, permitir que toolbars consuman eventos remanentes
        if not self.model.visible:
            return
        if getattr(self, 'items_toolbar_controller', None):
            if self.items_toolbar_controller.handle_event(event):
                return
        if getattr(self, 'items_add_remove_model', None) and getattr(self.items_add_remove_model, 'visible', False):
            if self.items_add_remove_controller.handle_event(event):
                return

    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
        # Ocultar todos los paneles mientras se mantiene presionado para centrar cámara
        if getattr(self.model, 'holding_pos_focus', False):
            return
        # Renderizar SIEMPRE el título del Items Editor (independiente del picker)
        title_rect: Optional[pygame.Rect] = None
        try:
            title_rect = self.title_controller.render(screen)
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController.draw] title render failed")
        # Renderizar toolbar principal temprano para actualizar su posición (se volverá a dibujar al final para asegurar z-order)
        try:
            if hasattr(self, 'items_toolbar_controller'):
                self.items_toolbar_controller.render(screen)
        except Exception:
            pass
        # Asegurar visibilidad de subpaneles si la visibilidad global cambió externamente (F7)
        # El Picker NO se fuerza visible aquí (lo controla el toolbar 'items_on_map')
        self.instances_controller.model.visible = True
        # Calcular rects de instancias para reservar altura exacta en el picker
        try:
            inst_list_rect, inst_params_rect = self.instances_controller.get_layout_rects()
        except Exception:
            inst_list_rect = inst_params_rect = None
        reserve_h = None
        margin = self.instances_controller.model.margin if hasattr(self.instances_controller, 'model') else 20
        if inst_list_rect:
            # Solo reservamos la altura de la lista inferior + margen de separación
            reserve_h = inst_list_rect.h + margin
        else:
            # Fallback aproximado: sólo una franja inferior (25% pantalla) + margen
            sw, sh = screen.get_size()
            reserve_h = (sh // 4) + margin
        # Inyectar en la vista del picker para su layout (usa title_rect externo)
        setattr(self.picker_controller.view, '_reserved_bottom_h', reserve_h)
        if title_rect is not None:
            setattr(self.picker_controller.view, 'title_rect', title_rect)
            # Alinear verticalmente el picker con la fila de toolbars (misma Y que toolbar): título.bottom + 8
            try:
                setattr(self.picker_controller.view, '_top_anchor_y', title_rect.bottom + 8)
            except Exception:
                pass
        # Si la sub-toolbar Add/Remove está visible, alinear el picker a su derecha
        try:
            if getattr(self.items_add_remove_model, 'visible', False):
                tbv = getattr(self, 'items_toolbar_view', None)
                arv = getattr(self, 'items_add_remove_view', None)
                if tbv is not None and arv is not None:
                    tb_widget = tbv.widget
                    tb_pos = getattr(tb_widget.panel, 'pos', None) or (tb_widget.x, tb_widget.y)
                    tb_panel_w = tb_widget.panel.surface.get_width()
                    # Posición X del Add/Remove (lo coloca a la derecha del toolbar con +8)
                    ar_x = tb_pos[0] + tb_panel_w + 8
                    ar_panel_w = arv.widget.panel.surface.get_width()
                    # Ancla izquierda del picker: pegado al borde derecho del Add/Remove (gap=0)
                    left_anchor_x = ar_x + ar_panel_w
                    setattr(self.picker_controller.view, '_left_anchor_x', left_anchor_x)
            else:
                # Limpiar ancla cuando no esté visible
                if hasattr(self.picker_controller.view, '_left_anchor_x'):
                    setattr(self.picker_controller.view, '_left_anchor_x', None)
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController.draw] failed to compute picker left anchor")
        # Inyectar flags de spawn al PickerPanelView para efectos visuales (parpadeo)
        try:
            setattr(self.picker_controller.view, '_spawn_mode_active', getattr(self.model, 'spawn_mode_active', False))
            setattr(self.picker_controller.view, '_spawn_item_id', getattr(self.model, 'spawn_item_id', None))
        except Exception:
            pass
        # Primero, dibujar picker (ya no dibuja el título por su cuenta)
        self.picker_controller.draw(screen)
        # Sincronizar hover tras render del picker
        self.model.hovered_item_id = self.picker_controller.model.hovered_item_id
        # Luego, dibujar propiedades usando el rect del título del Items Editor
        self.properties_controller.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)
        self.properties_controller.draw(screen, title_rect)
        # Finalmente, panel de instancias del mapa + params
        list_rect, params_rect = inst_list_rect, inst_params_rect
        # Log only when rects or reserved height change
        if (self._last_inst_list_rect != list_rect) or (self._last_inst_params_rect != params_rect) or (self._last_reserved_h != reserve_h):
            logging.getLogger(__name__).debug(
                f"[ItemsEditorController.draw] instances visible={self.instances_controller.model.visible} list_rect={list_rect} params_rect={params_rect} reserved_h={reserve_h}"
            )
            self._last_inst_list_rect = list_rect.copy() if list_rect else None
            self._last_inst_params_rect = params_rect.copy() if params_rect else None
            self._last_reserved_h = reserve_h
        self.instances_controller.draw(screen)
        # Finalmente, toolbars por encima
        try:
            self.items_toolbar_controller.render(screen)
            self.items_add_remove_controller.render(screen)
        except Exception:
            pass
        # Render standard drop hover (highlight + tooltip) under editor UI using the shared system
        try:
            if hasattr(self, 'game') and getattr(self.model, 'visible', False) and not getattr(self.model, 'holding_pos_focus', False):
                world = getattr(self.game, 'ecs', None)
                world = getattr(world, 'ecs_world', None)
                camera = getattr(self.game, 'camera', None)
                if world and camera and hasattr(self, '_hover_renderer') and self._hover_renderer:
                    self._hover_renderer.update(world, screen, camera)
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController.draw] hover render failed")
        # Resaltar ítem del mapa bajo el cursor en modo eliminar (borde rojo como en Entities)
        try:
            if getattr(self.model, 'delete_mode_active', False):
                mx, my = pygame.mouse.get_pos()
                world, camera = self._world_and_camera()
                if world and camera:
                    eid = self._find_drop_entity_at(mx, my)
                    if eid is not None:
                        comps = world.components
                        pos2 = comps['Position'][eid]
                        sprite = comps['Sprite'][eid]
                        scale_comp = comps.get('Scale', {}).get(eid)
                        scale = scale_comp.scale if scale_comp else 1.0
                        w, h = sprite.image.get_size()
                        w = int(w * scale * camera.zoom)
                        h = int(h * scale * camera.zoom)
                        sx2, sy2 = camera.apply((pos2.x, pos2.y))
                        rect = pygame.Rect(sx2, sy2, w, h)
                        overlay = pygame.Surface(rect.size, pygame.SRCALPHA)
                        overlay.fill((255, 0, 0, 80))
                        screen.blit(overlay, rect.topleft)
                        pygame.draw.rect(screen, (255, 0, 0), rect, 2)
        except Exception:
            pass
        # Overlays de ayuda del cursor (similar al Entities Editor)
        try:
            if getattr(self.model, 'spawn_mode_active', False):
                mx, my = pygame.mouse.get_pos()
                if getattr(self.model, 'spawn_item_id', None) is None:
                    msg = "Haz clic sobre un ítem"
                else:
                    msg = "Selecciona dónde posicionar el ítem en el mapa o sobre tu inventario"
                try:
                    surf = self.font.render(msg, True, (255, 255, 0))
                except Exception:
                    # Fallback a fuente por defecto si fuese necesario
                    f = pygame.font.SysFont(None, 18)
                    surf = f.render(msg, True, (255, 255, 0))
                screen.blit(surf, (mx + 10, my + 10))
            if getattr(self.model, 'delete_mode_active', False):
                mx, my = pygame.mouse.get_pos()
                msg = "Haz clic sobre el ítem del inventario, mapa o menú para poder eliminarlo"
                try:
                    surf = self.font.render(msg, True, (255, 0, 0))
                except Exception:
                    f = pygame.font.SysFont(None, 18)
                    surf = f.render(msg, True, (255, 0, 0))
                screen.blit(surf, (mx + 10, my + 10))
        except Exception:
            pass

    # --- Visibilidad ---
    def show(self) -> None:
        self.model.visible = True
        # El Picker se mantiene oculto hasta que se pulse 'items_on_map'
        self.picker_controller.model.visible = False
        self.instances_controller.model.visible = True
        # Refrescar datos de instancias al mostrar
        self.instances_controller.reload_data()
        # Toolbar principal visible por defecto, sub-toolbar oculto
        if hasattr(self, 'items_add_remove_model'):
            self.items_add_remove_model.visible = False
        if hasattr(self, 'items_toolbar_model'):
            self.items_toolbar_model.active_tool = None

    def hide(self) -> None:
        self.model.visible = False
        self.picker_controller.model.visible = False
        self.instances_controller.model.visible = False
        # Ocultar toolbars y limpiar selección
        if hasattr(self, 'items_add_remove_model'):
            self.items_add_remove_model.visible = False
            self.items_add_remove_model.active_tool = None
        if hasattr(self, 'items_toolbar_model'):
            self.items_toolbar_model.active_tool = None

    def toggle(self) -> None:
        if self.model.visible:
            self.hide()
        else:
            self.show()

    # --- Integraciones auxiliares ---
    def set_game(self, game: Any) -> None:
        """Permite a features del picker (RMB spawn) acceder al juego."""
        self.game = game
        self.picker_controller.game = game
        # El InstancesPanel ahora puede solicitar enfoque de cámara
        self.instances_controller.game = game

    # API para ToolbarView: consultar si una herramienta está activa (principal o sub-toolbar)
    def is_active(self, tool: str) -> bool:
        try:
            if getattr(self.items_toolbar_model, 'active_tool', None) == tool:
                return True
        except Exception:
            pass
        try:
            return getattr(self.items_add_remove_model, 'active_tool', None) == tool
        except Exception:
            return False

    # --- Spawn/Delete modes ---
    def enter_spawn_mode(self) -> None:
        """Enter item spawn mode: enable picker visibility and reset selection."""
        # Cancel delete mode if active
        if self.model.delete_mode_active:
            self.exit_delete_mode()
        self.model.spawn_mode_active = True
        self.model.spawn_item_id = None
        # Ensure picker is visible to select an item to spawn
        self.picker_controller.model.visible = True

    def exit_spawn_mode(self) -> None:
        """Exit item spawn mode and restore cursor."""
        self.model.spawn_mode_active = False
        self.model.spawn_item_id = None
        try:
            pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW)
        except Exception:
            pass

    def enter_delete_mode(self) -> None:
        """Enter delete mode for item drops on the map."""
        # Cancel spawn mode if active
        if self.model.spawn_mode_active:
            self.exit_spawn_mode()
        self.model.delete_mode_active = True
        try:
            pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_CROSSHAIR)
        except Exception:
            pass

    def exit_delete_mode(self) -> None:
        """Exit delete mode and restore cursor."""
        self.model.delete_mode_active = False
        try:
            pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_ARROW)
        except Exception:
            pass

    # --- Item spawn/delete operations ---
    def _world_and_camera(self):
        game = getattr(self, 'game', None)
        if not game or not hasattr(game, 'ecs'):
            return None, None
        world = getattr(game.ecs, 'ecs_world', None)
        camera = getattr(game, 'camera', None)
        return world, camera

    def _find_drop_entity_at(self, sx: int, sy: int) -> Optional[int]:
        """Return entity id of topmost drop under screen coords, or None."""
        world, camera = self._world_and_camera()
        if not world or not camera:
            return None
        comps = world.components
        hovered = None
        max_layer = -float('inf')
        try:
            for eid in world.get_entities_in_camera(camera, 'PhysicalItemComponent', 'Sprite', 'Position', 'ZLayer'):
                pos2 = comps['Position'][eid]
                sprite = comps['Sprite'][eid]
                scale_comp = comps.get('Scale', {}).get(eid)
                scale = scale_comp.scale if scale_comp else 1.0
                w, h = sprite.image.get_size()
                w = int(w * scale * camera.zoom)
                h = int(h * scale * camera.zoom)
                sx2, sy2 = camera.apply((pos2.x, pos2.y))
                rect = pygame.Rect(sx2, sy2, w, h)
                if rect.collidepoint(sx, sy):
                    layer = comps['ZLayer'][eid].layer
                    if layer >= max_layer:
                        hovered = eid
                        max_layer = layer
        except Exception:
            pass
        return hovered

    def spawn_item_at_screen_pos(self, sx: int, sy: int) -> bool:
        """Spawn currently selected item at given screen coords. Returns True if spawned."""
        if not self.model.spawn_item_id:
            return False
        world, camera = self._world_and_camera()
        if not world or not camera:
            return False
        # Convert screen to world
        wx = sx / camera.zoom + camera.offset_x
        wy = sy / camera.zoom + camera.offset_y
        tile_x = int(wx) // TILE_SIZE
        tile_y = int(wy) // TILE_SIZE
        zone_id = get_zone_for_tile(tile_x, tile_y)
        inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        drop_manager = ItemDropManager(inv_map_path)
        drop_id = uuid.uuid4().hex
        drop_manager.create_drop(drop_id, self.model.spawn_item_id, 1, zone_id, position={'x': wx, 'y': wy})
        try:
            InventoryPickupSystem.recently_created.add(drop_id)
        except Exception:
            pass
        # Immediate spawn into ECS to visualize instantly
        try:
            self._spawn_drop_entity_now(world, drop_id, self.model.spawn_item_id, 1, zone_id, wx, wy)
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] immediate spawn failed")
        # Refresh instances list
        try:
            self.instances_controller.reload_data()
        except Exception:
            pass
        return True

    def _spawn_drop_entity_now(self, world, drop_id: str, item_id: str, quantity: int, zone_id: str, x: float, y: float) -> None:
        # Avoid double-spawn from MapLoadDropsSystem by marking as spawned there too
        try:
            mlds = next((s for s in getattr(world, 'update_systems', []) if isinstance(s, MapLoadDropsSystem)), None)
            if mlds:
                mlds._spawned.add(drop_id)
        except Exception:
            pass
        # Load item models once
        if not hasattr(self, '_items_models') or not self._items_models:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
            try:
                self._items_models = load_items(items_path)
            except Exception:
                self._items_models = {}
        model = self._items_models.get(item_id)
        eid = world.create_entity()
        world.components['PhysicalItemComponent'][eid] = PhysicalItemComponent(drop_id, item_id, quantity, zone_id)
        world.components['Position'][eid] = Position(x, y)
        world.components['CollectibleComponent'][eid] = CollectibleComponent()
        layer = getattr(model, 'z_layer', None) or DEFAULT_Z
        world.components['ZLayer'][eid] = ZLayer(layer)
        if model:
            icon = getattr(model, 'icon_small', None) or getattr(model, 'icon', None)
            if isinstance(icon, list):
                icon = icon[0]
            if icon:
                world.components['Sprite'][eid] = Sprite(icon)
                world.components['Scale'][eid] = Scale(getattr(model, 'scale_map', 1.0))

    def delete_drop_at_screen_pos(self, sx: int, sy: int) -> bool:
        """Delete a drop under the cursor. Returns True if one was deleted."""
        world, camera = self._world_and_camera()
        if not world or not camera:
            return False
        eid = self._find_drop_entity_at(sx, sy)
        if eid is None:
            return False
        comps = world.components
        phys = comps.get('PhysicalItemComponent', {}).get(eid)
        if not phys:
            return False
        inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        drop_manager = ItemDropManager(inv_map_path)
        ok = drop_manager.pick_up(phys.drop_id)
        if ok:
            try:
                world.remove_entity(eid)
            except Exception:
                pass
            try:
                self.instances_controller.reload_data()
            except Exception:
                pass
            return True
        return False

