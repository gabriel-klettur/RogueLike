import pygame
import logging
from typing import Any, Dict, Optional

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
from .items_title_panel.items_title_controller import ItemsTitleController
from .items_properties_panel.items_properties_panel_controller import ItemsPropertiesPanelController
from .items_instances_panel.items_instances_panel_controller import ItemsInstancesPanelController
from roguelike_game.ecs.systems.rendering.drop_hover_system import DropHoverRenderSystem
from .rendering.items_editor_renderer import ItemsEditorRenderer
from .services.drop_service import DropService
from .services.callbacks_service import ItemsEditorCallbacks
from .services.assets_service import ItemsAssetsService
from .services.modes_service import ItemsModesService
from .services.visibility_service import ItemsVisibilityService
from .services.items_system_service import ItemsSystemService


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
        self.title_controller = ItemsTitleController(self.model)

        # Servicios auxiliares y renderer
        self.drop_service = DropService(self)
        self.renderer = ItemsEditorRenderer(self)
        self.callbacks = ItemsEditorCallbacks(self)
        self.assets_service = ItemsAssetsService(self)
        self.modes = ItemsModesService(self)
        self.visibility = ItemsVisibilityService(self)
        self.items_system = ItemsSystemService(self)

        # Callbacks unificados de selección
        # Wire select from picker and from instances list
        self.picker_controller.on_select_id = self.callbacks.set_selected_item
        self.picker_controller.on_open_id = self.callbacks.on_open_id
        self.instances_controller.on_select_item_id = self.callbacks.set_selected_item

        # Press-and-hold focus from instances list
        self.instances_controller.on_start_hold_focus = self.callbacks.start_hold_focus
        self.instances_controller.on_end_hold_focus = self.callbacks.end_hold_focus

        # Back-reference so the properties panel can call editor methods (confirm flow)
        try:
            self.properties_controller.editor_controller = self
        except Exception:
            pass

        # Delegar spawn RMB desde el picker hacia DropService
        self.picker_controller.on_spawn_at_player = self.drop_service.spawn_at_player

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
        self.properties_controller.get_assets_anchor_rect = self.assets_service.get_assets_anchor_rect
        self.properties_controller.on_asset_changed = self.assets_service.on_asset_changed
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
        self.renderer.draw(screen)

    # --- Visibilidad ---
    def show(self) -> None:
        self.visibility.show()

    def hide(self) -> None:
        self.visibility.hide()

    def toggle(self) -> None:
        self.visibility.toggle()

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
        """Recarga items.json y assets y sincroniza caches a través del servicio."""
        try:
            from .services.item_catalog_service import refresh_items_catalog
            refresh_items_catalog(self)
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorController] Failed to refresh items catalog via service")

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
        return self.items_system.delete_item_from_system(item_id)

    # --- Spawn/Delete modes ---
    def enter_spawn_mode(self) -> None:
        """Enter item spawn mode: enable picker visibility and reset selection."""
        self.modes.enter_spawn_mode()

    def exit_spawn_mode(self) -> None:
        """Exit item spawn mode and restore cursor."""
        self.modes.exit_spawn_mode()

    def enter_delete_mode(self) -> None:
        """Enter delete mode for item drops on the map."""
        self.modes.enter_delete_mode()

    def exit_delete_mode(self) -> None:
        """Exit delete mode and restore cursor."""
        self.modes.exit_delete_mode()

    # --- Add Item On System mode (UI orchestration) ---
    def enter_add_items_on_system_mode(self) -> None:
        """Hide the picker and let Properties panel take focus/space while adding items to the system."""
        self.modes.enter_add_items_on_system_mode()

    def exit_add_items_on_system_mode(self) -> None:
        """Restore picker visibility and clear transient flags."""
        self.modes.exit_add_items_on_system_mode()

    # --- Item spawn/delete operations ---
    def spawn_item_at_screen_pos(self, sx: int, sy: int) -> bool:
        return self.drop_service.spawn_item_at_screen_pos(sx, sy)

    def delete_drop_at_screen_pos(self, sx: int, sy: int) -> bool:
        return self.drop_service.delete_drop_at_screen_pos(sx, sy)
