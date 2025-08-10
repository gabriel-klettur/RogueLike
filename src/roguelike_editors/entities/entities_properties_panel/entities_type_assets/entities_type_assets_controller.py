import pygame

from roguelike_editors.entities.entities_properties_panel.entities_type_assets.entities_type_assets_model import EntitiesTypeAssetsModel
from roguelike_editors.entities.entities_properties_panel.entities_type_assets.entities_type_assets_view import EntitiesTypeAssetsView
from roguelike_editors.entities.entities_properties_panel.entities_type_assets.entities_type_assets_events import EntitiesTypeAssetsEventHandler

class EntitiesTypeAssetsController:
    """Controller for main 'properties'/'assets' tabs."""
    def __init__(self, parent_model, font: pygame.font.Font):
        # parent_model is EntityPropertiesPanelModel
        self.model = EntitiesTypeAssetsModel(parent_model)
        self.view = EntitiesTypeAssetsView(font)
        self.event_handler = EntitiesTypeAssetsEventHandler(self)

    def reset_parent_ui_state(self) -> None:
        """Clear transient selection/editing state in the parent model.

        This is called when switching the main type tab to avoid stale UI state.
        """
        pm = self.model.parent_model
        pm.focused_property = None
        pm.editing_property = None
        pm.hovered_property = None

    def draw(self, screen: pygame.Surface) -> None:
        """Delegates drawing of main tabs."""
        self.view.draw(screen, self.model)

    def handle_event(self, event: pygame.event.Event) -> bool:
        """Delegates event handling for main tabs."""
        return self.event_handler.handle_event(event)
