import pygame
pygame.font.init()

from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_model import EntityPickerPanelModel
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_view import EntityPickerPanelView


from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_events import EntitiesPickerEventHandler



class EntityPickerPanelController:
    """Controller para editor de entidades: jugador y monstruos."""
    def __init__(self, player_stats: dict[str, any], monsters: dict[str, any], assets: dict[str, pygame.Surface], font: pygame.font.Font):
        self.model = EntityPickerPanelModel(player_stats=player_stats, monsters=monsters, assets=assets)
        self.view = EntityPickerPanelView(assets, font)

        self.event_handler = EntitiesPickerEventHandler(self)

    def handle_event(self, event: pygame.event.Event) -> None:
        self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
        self.view.draw(screen, self.model)



