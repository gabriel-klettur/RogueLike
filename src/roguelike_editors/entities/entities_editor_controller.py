import pygame

from pathlib import Path
from roguelike_ui.services.json_persistence import load_from_json

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

class EntitiesEditorController:
    """
    Controlador principal del editor de entidades en arquitectura MVC.
    Orquesta modelos y subcontrollers (title, toolbar, add/remove, picker, properties).
    """
    def __init__(self, model: EntitiesEditorModel, font: pygame.font.Font):
        self.model = model
        self.font = font
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
            self.model.player_stats, self.model.monsters, self.model.assets, self.font
        )
        # Properties
        self.properties_controller = EntityPropertiesPanelController(
            self.model.player_stats, self.model.monsters, self.font
        )
        # Vista (separa render)
        from roguelike_editors.entities.entities_editor_view import EntitiesEditorView
        self.view = EntitiesEditorView(self)

    def is_active(self, tool: str) -> bool:
        """Retorna True si la herramienta está activa en el toolbar."""
        return self.model.toolbar_model.active_tool == tool

    def handle_event(self, event: pygame.event.Event) -> bool:
        """
        Delega el evento a los subcontrollers en orden de prioridad.
        Retorna True si fue consumido.
        """
        if self.title_controller.handle_event(event):
            return True
        if self.toolbar_controller.handle_event(event):
            return True
        active = self.model.toolbar_model.active_tool
        if active in ('entities_on_map', 'entities_on_system'):
            # Add/Remove panel
            if self.add_remove_controller.handle_event(event):
                return True
            # Picker panel
            self.picker_controller.handle_event(event)
            # Properties panel
            # Sincronizar seleccionado
            self.properties_controller.model.selected_id = self.picker_controller.model.selected_id
            if self.properties_controller.handle_event(event):
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
