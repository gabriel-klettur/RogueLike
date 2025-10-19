import pygame

import logging
logger = logging.getLogger(__name__)
from roguelike_editors.entities.services.constants import UI_MARGIN
from roguelike_editors.entities.services.history import HistoryManager
from roguelike_editors.entities.services.camera_helpers import screen_to_tile  # re-exported for tests
from roguelike_editors.entities.services.entity_lookup import find_clickable_entity_at  # re-exported for tests
from roguelike_editors.entities.controller.modes import EditorModes
from roguelike_editors.entities.controller.creation import open_new_monster_properties as _open_new_monster_properties
from roguelike_editors.entities.controller.events.keyboard import handle_keyboard
from roguelike_editors.entities.controller.events.panels import handle_panels
from roguelike_editors.entities.controller.events.entities_tools import handle_entities_tools
from roguelike_editors.entities.controller.events.map_events import handle_map_interactions

from roguelike_editors.entities.entities_editor_model import EntitiesEditorModel
from roguelike_editors.entities.entities_title.entities_title_controller import EntitiesTitleController
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_controller import EntitiesToolBarPanelController
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_view import EntitiesToolBarPanelView
from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_events import EntitiesToolBarPanelEventHandler
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_controller import EntitiesAddRemovePanelController
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_view import EntitiesAddRemovePanelView
from roguelike_editors.entities.entities_add_remove_panel.entities_add_remove_panel_events import EntitiesAddRemovePanelEventHandler
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_controller import EntityPickerPanelController
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_controller import EntityPropertiesPanelController
from roguelike_editors.entities.entities_tutorial_panel.entities_tutorial_panel_controller import EntitiesTutorialPanelController

class EntitiesEditorController:
    """
    Controlador principal del editor de entidades en arquitectura MVC.
    Orquesta modelos y subcontrollers (title, toolbar, add/remove, picker, properties).
    """
    def __init__(self, model: EntitiesEditorModel, font: pygame.font.Font):
        self.model = model
        self.font = font
        # History manager for undo/redo
        self.history = HistoryManager()
        # Título
        self.title_controller = EntitiesTitleController(self, self.model.title_model, self.font)
        # Toolbar
        self.toolbar_event_handler = EntitiesToolBarPanelEventHandler(self, self.model.toolbar_model)
        self.toolbar_view = EntitiesToolBarPanelView(self, self.model.toolbar_model)
        self.toolbar_controller = EntitiesToolBarPanelController(
            self, self.model.toolbar_model, self.toolbar_view, self.toolbar_event_handler
        )
        # Add/Remove
        self.add_remove_event_handler = EntitiesAddRemovePanelEventHandler(self, self.model.add_remove_model)
        self.add_remove_view = EntitiesAddRemovePanelView(self, self.model.add_remove_model)
        self.add_remove_controller = EntitiesAddRemovePanelController(
            self, self.model.add_remove_model, self.add_remove_view, self.add_remove_event_handler
        )
        # Picker
        self.picker_controller = EntityPickerPanelController(
            self.model.player_stats, self.model.hostiles, self.model.neutrals, self.model.assets, self.font
        )
        # Inicializar posición del picker panel a la derecha del add/remove panel
        margin = UI_MARGIN
        add_rem_widget = self.add_remove_view.widget
        add_pos = add_rem_widget.panel.pos or (add_rem_widget.x, add_rem_widget.y)
        add_w, _ = add_rem_widget.panel.surface.get_size()
        self.picker_controller.view.x = add_pos[0] + add_w + margin
        self.picker_controller.view.y = add_pos[1]
        # Properties
        self.properties_controller = EntityPropertiesPanelController(
            self, self.model.player_stats, self.model.monsters, self.model.player_assets, self.font
        )
        # Vista (separa render)
        from roguelike_editors.entities.entities_editor_view import EntitiesEditorView
        self.view = EntitiesEditorView(self)
        # Tutorial panel controller
        self.tutorial_controller = EntitiesTutorialPanelController(self)
        # Modes helper
        self.modes = EditorModes(self)

    def open_new_monster_properties(self) -> None:
        """
        Create a new blank monster class entry in-memory and open the Properties Panel
        for editing its fields (including assigning a new id).
        """
        _open_new_monster_properties(self)

    def open_new_hostile_properties(self) -> None:
        """Alias de compatibilidad: abre creación de Hostile (antes Monster)."""
        self.open_new_monster_properties()

    def enter_spawn_mode(self, entity_type=None):
        """
        Inicia modo spawn de entidades: picker parpadeante y selección inicial.
        """
        self.modes.enter_spawn_mode(entity_type)

    def exit_spawn_mode(self):
        """
        Sale de modo spawn de entidades.
        """
        self.modes.exit_spawn_mode()

    def enter_delete_mode(self):
        """
        Entra en modo borrar entidades.
        """
        self.modes.enter_delete_mode()

    def exit_delete_mode(self):
        """
        Sale de modo borrar entidades.
        """
        self.modes.exit_delete_mode()

    def enter_add_entities_on_system_mode(self) -> None:
        """Hide picker and expand Properties panel to occupy picker's space."""
        self.modes.enter_add_entities_on_system_mode()

    def exit_add_entities_on_system_mode(self) -> None:
        """Restore picker visibility and Properties panel layout."""
        self.modes.exit_add_entities_on_system_mode()

    def is_active(self, tool: str) -> bool:
        """Retorna True si la herramienta está activa en el toolbar."""
        return self.model.toolbar_model.active_tool == tool

    def handle_event(self, event: pygame.event.Event) -> bool:
        # Debug global entities_on_map: click event recibido
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            logger.debug(f" Click global en {event.pos}, spawn_mode={self.model.spawn_mode_active}, spawn_entity_type={self.model.spawn_entity_type}")

        """
        Delega el evento a los subcontrollers en orden de prioridad.
        Retorna True si fue consumido.
        """
        if handle_keyboard(self, event):
            return True
        if handle_panels(self, event):
            return True
        # Ensure entities_tools uses the potentially monkeypatched symbols from this module
        try:
            import roguelike_editors.entities.controller.events.entities_tools as _et_mod
            _et_mod.screen_to_tile = screen_to_tile
            _et_mod.find_clickable_entity_at = find_clickable_entity_at
        except Exception:
            pass
        if handle_entities_tools(self, event):
            return True
        if handle_map_interactions(self, event):
            return True
        return False

    def update(self, camera, game_map=None):
        """
        Actualiza la lógica de panning si es necesario.
        """
        # Implementar si el editor necesita actualizar algo continuo
        pass

    def render(self, screen: pygame.Surface) -> None:
        """
        Delegar render a la vista especializada.
        """
        self.view.render(screen)
        # Renderizar tutorial por encima
        try:
            self.tutorial_controller.render(screen)
        except Exception:
            pass
