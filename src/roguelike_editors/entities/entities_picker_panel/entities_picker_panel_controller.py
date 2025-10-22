import pygame
pygame.font.init()

from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_model import EntityPickerPanelModel
from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_view import EntityPickerPanelView


from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_events import EntitiesPickerEventHandler
import logging
logger = logging.getLogger(__name__)


class EntityPickerPanelController:
    """Controller para editor de entidades: jugadores, hostiles, neutrales y specials."""
    def __init__(self, player_stats: dict[str, any], monsters: dict[str, any], neutrals: dict[str, any], specials: dict[str, any], assets: dict[str, pygame.Surface], font: pygame.font.Font):
        # EntityPickerPanelModel ahora recibe también 'neutrals' y 'specials'
        self.model = EntityPickerPanelModel(player_stats=player_stats, hostiles=monsters, neutrals=neutrals, specials=specials, assets=assets)
        self.view = EntityPickerPanelView(assets, font)

        self.event_handler = EntitiesPickerEventHandler(self)

    def handle_event(self, event: pygame.event.Event) -> None:
        # Debug picker: evento recibido
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            logger.debug(f" Click en picker en {event.pos}, blink={self.model.blink}, selected_id={self.model.selected_id}")
        self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
        self.view.draw(screen, self.model)



