import pygame
import logging
from typing import Any, Dict, Optional
from types import SimpleNamespace

from .items_editor_models import ItemsEditorModel
from .items_editor_events import ItemsEditorEvents
from .items_editor_view import ItemsEditorView
from .items_tutorial_panel import ItemsTutorialPanelController
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
import json
from roguelike_ui.services.json_persistence import load_from_json


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

        # Tutorial controller (MVC) for Items Editor
        self.tutorial_controller = ItemsTutorialPanelController(self, self.view)

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
            # Si está activo 'remove_item' o delete_mode_active, eliminar del sistema en lugar de seleccionar
            try:
                if getattr(self.items_add_remove_model, 'active_tool', None) == 'remove_item' or \
                   getattr(self.model, 'delete_mode_active', False):
                    ok = self.delete_item_from_system(item_id)
                    if ok:
                        logging.getLogger(__name__).info("[ItemsEditorController] Deleted item '%s' from system via picker", item_id)
                        # Limpiar selección ya que el ítem ya no existe
                        self.model.selected_item_id = None
                        self.picker_controller.model.selected_item_id = None
                        # Forzar refresco de contexto de propiedades (queda vacío)
                        self.properties_controller.update_context(self.model.items, None, self.model.hovered_item_id)
                        try:
                            self.instances_controller.reload_data()
                        except Exception:
                            pass
                        return
            except Exception:
                logging.getLogger(__name__).exception("[ItemsEditorController] remove_item via picker failed")
            # Actualizar SSOT del editor y del picker para resaltar en la grilla
            self.model.selected_item_id = item_id
            self.picker_controller.model.selected_item_id = item_id
            # Sincronizar inmediatamente el panel de propiedades para mostrar el ítem seleccionado
            self.properties_controller.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)
            # Tutorial pulse: user selected an item in the picker
            try:
                setattr(self.model, 'tutorial_spawn_selection_pulse', True)
            except Exception:
                pass

        def _on_open_id(item_id: str) -> None:
            # Si estamos en modo eliminar, borrar directamente al abrir
            try:
                if getattr(self.items_add_remove_model, 'active_tool', None) == 'remove_item' or \
                   getattr(self.model, 'delete_mode_active', False):
                    ok = self.delete_item_from_system(item_id)
                    if ok:
                        logging.getLogger(__name__).info("[ItemsEditorController] Deleted item '%s' from system via open", item_id)
                        self.model.selected_item_id = None
                        self.picker_controller.model.selected_item_id = None
                        self.properties_controller.update_context(self.model.items, None, self.model.hovered_item_id)
                        try:
                            self.instances_controller.reload_data()
                        except Exception:
                            pass
                        return
            except Exception:
                logging.getLogger(__name__).exception("[ItemsEditorController] remove_item via open failed")
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
                logging.getLogger(__name__).info("[ItemsEditorController] Focusing camera at (%.2f, %.2f)", x, y)
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
                    logging.getLogger(__name__).info("[ItemsEditorController] Restoring camera to player at (%.2f, %.2f)", pos.x, pos.y)
                    self.game.camera.update(SimpleNamespace(x=pos.x, y=pos.y))
                self.model.holding_pos_focus = False
            except Exception:
                logging.getLogger(__name__).exception("[ItemsEditorController] end_hold_focus failed")

        self.instances_controller.on_start_hold_focus = _start_hold_focus
        self.instances_controller.on_end_hold_focus = _end_hold_focus

        # Back-reference so the properties panel can call editor methods (confirm flow)
        try:
            self.properties_controller.editor_controller = self
        except Exception:
            pass

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
        # Notificación tras commit de edición de propiedades (incluye rename de id)
        self.properties_controller.on_after_commit_edit = self._on_after_commit_edit

        # Hover rendering system (reuse in editor to match in-game behavior)
        try:
            self._hover_renderer = DropHoverRenderSystem(perf_log=None)
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] Failed to init DropHoverRenderSystem")

    # --- Ciclo principal ---
    def handle_event(self, event: pygame.event.Event) -> None:
        # 1) Dar prioridad al Tutorial: ESC y clicks dentro del panel deben consumirse aquí
        try:
            if getattr(self, 'tutorial_controller', None) and self.tutorial_controller.handle_event(event):
                return
        except Exception:
            pass
        # 2) Enrutador general del Items Editor (F7/ESC y el resto de la UI)
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
                    # Si estamos en modo add_item_on_system, anclar también el panel de propiedades al borde derecho del Add/Remove
                    try:
                        if getattr(self.properties_controller.model, 'show_add_system_selector', False) or \
                           getattr(self.items_add_remove_model, 'active_tool', None) == 'add_item_on_system':
                            setattr(self.properties_controller.view, '_left_anchor_x', left_anchor_x)
                            # Alinear Y con la fila de toolbars
                            top_anchor_y = (title_rect.bottom + 8) if title_rect is not None else None
                            setattr(self.properties_controller.view, '_top_anchor_y', top_anchor_y)
                        else:
                            # Limpiar anclas cuando no esté activo el modo
                            if hasattr(self.properties_controller.view, '_left_anchor_x'):
                                setattr(self.properties_controller.view, '_left_anchor_x', None)
                            if hasattr(self.properties_controller.view, '_top_anchor_y'):
                                setattr(self.properties_controller.view, '_top_anchor_y', None)
                    except Exception:
                        pass
            else:
                # Limpiar ancla cuando no esté visible
                if hasattr(self.picker_controller.view, '_left_anchor_x'):
                    setattr(self.picker_controller.view, '_left_anchor_x', None)
                # Y limpiar anclas del panel de propiedades salvo que el modo especial fuerce su visibilidad
                try:
                    if not getattr(self.properties_controller.model, 'show_add_system_selector', False):
                        if hasattr(self.properties_controller.view, '_left_anchor_x'):
                            setattr(self.properties_controller.view, '_left_anchor_x', None)
                        if hasattr(self.properties_controller.view, '_top_anchor_y'):
                            setattr(self.properties_controller.view, '_top_anchor_y', None)
                except Exception:
                    pass
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
        # Render tutorial panel last so highlights and panel are on top
        try:
            if getattr(self, 'tutorial_controller', None):
                self.tutorial_controller.render(screen)
        except Exception:
            pass
        # Render standard drop hover (highlight + tooltip) under editor UI using the shared system
        # Evitar duplicación: si el mundo ya tiene DropHoverRenderSystem, no dibujar el del editor
        try:
            if hasattr(self, 'game') and getattr(self.model, 'visible', False) and not getattr(self.model, 'holding_pos_focus', False):
                world_obj = getattr(self.game, 'ecs', None)
                world = getattr(world_obj, 'ecs_world', None)
                camera = getattr(self.game, 'camera', None)
                if world and camera:
                    systems_u = list(getattr(world, 'update_systems', []))
                    systems_r = list(getattr(world, 'render_systems', []))
                    has_world_hover = any(isinstance(s, DropHoverRenderSystem) for s in (systems_u + systems_r))
                    if not has_world_hover and hasattr(self, '_hover_renderer') and self._hover_renderer:
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

    def _on_after_commit_edit(self, key: str, old_id: str, new_id: Optional[str], value: Any) -> None:
        """Callback desde PropertiesPanel tras persistir un cambio.

        - Si se renombra 'id', actualiza la selección al nuevo id.
        - Refresca el catálogo de ítems y caches globales para que los spawns usen datos actualizados.
        """
        try:
            if key == 'id' and new_id and new_id != old_id:
                # Mantener selección coherente en editor y picker
                if self.model.selected_item_id == old_id:
                    self.model.selected_item_id = new_id
                try:
                    if getattr(self.picker_controller.model, 'selected_item_id', None) == old_id:
                        self.picker_controller.model.selected_item_id = new_id
                except Exception:
                    pass
        finally:
            # Siempre refrescar el catálogo tras un commit (cualquier propiedad puede afectar spawns)
            try:
                self._refresh_items_catalog()
            except Exception:
                logging.getLogger(__name__).exception("[ItemsEditorController] Failed to refresh items catalog after edit")
            # Tutorial pulse: properties saved
            try:
                setattr(self.model, 'tutorial_properties_saved_pulse', True)
            except Exception:
                pass

    def _refresh_items_catalog(self) -> None:
        """Recarga items.json y assets, y sincroniza caches en:
        - Editor (model, picker, propiedades)
        - Game caches (game.items, game.item_assets)
        - Sistemas ECS que cachean ítems (MapLoadDropsSystem, ConsumeSystem)
        - Cache de spawns inmediatos del editor
        """
        try:
            from roguelike_game.managers.items.loader import ItemsLoader
            loader = ItemsLoader()
            items, assets = loader.load()
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] ItemsLoader.load() failed")
            return

        # Actualizar modelo del editor
        self.model.items = items
        self.model.assets = assets
        # Propagar a subcontroladores que puedan mantener referencias
        try:
            self.picker_controller.model.items = self.model.items
        except Exception:
            pass
        try:
            # Actualizar también assets en controller y view del picker
            self.picker_controller.model.assets = self.model.assets
            self.picker_controller.view.assets = self.model.assets
        except Exception:
            pass
        try:
            self.properties_controller.set_items(self.model.items)
        except Exception:
            pass

        # Actualizar caches globales del juego
        if hasattr(self, 'game') and self.game is not None:
            try:
                self.game.items = items
                self.game.item_assets = assets
            except Exception:
                pass
            # Actualizar sistemas ECS que cachean ítems
            try:
                world = getattr(getattr(self.game, 'ecs', None), 'ecs_world', None)
                if world:
                    # Importar aquí para evitar dependencias circulares en tope de archivo
                    from roguelike_game.ecs.systems.inventory.map_load_drops_system import MapLoadDropsSystem
                    from roguelike_game.ecs.systems.items.consume_system import ConsumeSystem
                    from roguelike_game.ecs.systems.rendering.drop_hover_system import DropHoverRenderSystem
                    from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem
                    from roguelike_game.ecs.systems.inventory.inventory_editor_system import InventoryEditorSystem
                    from roguelike_game.ecs.components.transform.scale import Scale
                    # update systems
                    for sys in list(getattr(world, 'update_systems', [])):
                        try:
                            if isinstance(sys, MapLoadDropsSystem):
                                sys.items = items
                            elif isinstance(sys, ConsumeSystem):
                                sys.items = items
                            elif isinstance(sys, DropHoverRenderSystem):
                                sys.items = items
                            elif isinstance(sys, InventoryUISystem):
                                sys.items = items
                                try:
                                    # Reemplazar superficies de íconos con assets recargados
                                    sys.icon_surfaces = {iid: assets.get(iid) for iid in items.keys()}
                                except Exception:
                                    pass
                            elif isinstance(sys, InventoryEditorSystem):
                                sys.items = items
                                try:
                                    # Invalidate scaled cache para forzar recarga
                                    sys.images = {}
                                except Exception:
                                    pass
                        except Exception:
                            pass
                    # render systems
                    for sys in list(getattr(world, 'render_systems', [])):
                        try:
                            if isinstance(sys, DropHoverRenderSystem):
                                sys.items = items
                            elif isinstance(sys, InventoryUISystem):
                                sys.items = items
                                try:
                                    sys.icon_surfaces = {iid: assets.get(iid) for iid in items.keys()}
                                except Exception:
                                    pass
                            elif isinstance(sys, InventoryEditorSystem):
                                sys.items = items
                                try:
                                    sys.images = {}
                                except Exception:
                                    pass
                        except Exception:
                            pass

                    # Actualizar sprites/escala de drops ya spawneados que correspondan con el item
                    try:
                        comps = world.components
                        phys_map = comps.get('PhysicalItemComponent', {})
                        sprite_map = comps.get('Sprite', {})
                        scale_map = comps.get('Scale', {})
                        for eid, phys in list(phys_map.items()):
                            spr = sprite_map.get(eid)
                            if spr is None:
                                continue
                            model = items.get(phys.item_id)
                            # Actualizar imagen del sprite si hay asset cargado
                            new_img = assets.get(phys.item_id)
                            if new_img is not None:
                                try:
                                    spr.image = new_img
                                except Exception:
                                    pass
                            # Actualizar escala según modelo
                            try:
                                new_scale = getattr(model, 'scale_map', None)
                                if new_scale is not None:
                                    sc = scale_map.get(eid)
                                    if sc is None:
                                        scale_map[eid] = Scale(new_scale)
                                    else:
                                        sc.scale = new_scale
                            except Exception:
                                pass
                    except Exception:
                        logging.getLogger(__name__).exception("[ItemsEditorController] Failed to update existing drop sprites after edit")
            except Exception:
                logging.getLogger(__name__).exception("[ItemsEditorController] Failed updating ECS systems items cache")

        # Actualizar cache interna usada para spawn inmediato
        try:
            self._items_models = items
        except Exception:
            pass

        # Refrescar renderer de hover del editor (si existe)
        try:
            if hasattr(self, '_hover_renderer') and self._hover_renderer is not None:
                self._hover_renderer.items = items
        except Exception:
            pass

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

    # --- Item system operations ---
    def delete_item_from_system(self, item_id: str) -> bool:
        """Remove an item entry from data/items/items.json by id. Returns True if removed."""
        try:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
            data = load_from_json(items_path)
            if item_id not in data:
                logging.getLogger(__name__).warning("[ItemsEditorController] delete_item_from_system: '%s' not found", item_id)
                return False
            # Remove entry and write back
            del data[item_id]
            with open(items_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            # Refrescar catálogos/caches y vista del picker
            try:
                self._refresh_items_catalog()
            except Exception:
                logging.getLogger(__name__).exception("[ItemsEditorController] Failed to refresh after deleting '%s'", item_id)
            return True
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] delete_item_from_system failed for '%s'", item_id)
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
        # Asegurar que el picker esté visible para elegir qué ítem del sistema eliminar
        try:
            self.picker_controller.model.visible = True
        except Exception:
            pass
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

    # --- Add Item On System mode (UI orchestration) ---
    def enter_add_items_on_system_mode(self) -> None:
        """Hide the picker and let Properties panel take focus/space while adding items to the system."""
        # Hide picker panel during this mode
        try:
            self.picker_controller.model.visible = False
        except Exception:
            pass
        # If the properties panel had any transient layout overrides from previous sessions, clear them
        try:
            pp_model = self.properties_controller.model
            # No draggable panel in Items view; keep potential placeholders consistent
            setattr(pp_model, 'expand_into_picker_space', True)
        except Exception:
            pass

    def exit_add_items_on_system_mode(self) -> None:
        """Restore picker visibility and clear transient flags."""
        # Show picker again
        try:
            self.picker_controller.model.visible = True
        except Exception:
            pass
        # Clear temporary flags
        try:
            pp_model = self.properties_controller.model
            setattr(pp_model, 'expand_into_picker_space', False)
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

