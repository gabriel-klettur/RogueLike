import pygame
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_model import AssetsGridPanelModel
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_view import AssetsGridPanelView
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_events import AssetsGridPanelEventHandler

class AssetsGridPanelController:
    """Controller para el panel de cuadrícula de assets en el panel de propiedades."""
    def __init__(self, parent_panel_model, font: pygame.font.Font):
        # parent_panel_model es EntityPropertiesPanelModel para posicion y panel_rect
        self.parent_model = parent_panel_model
        self.model = AssetsGridPanelModel()
        self.view = AssetsGridPanelView(font)
        # Referencia al modelo principal para state tabs
        self.view.parent_model = parent_panel_model
        self.event_handler = AssetsGridPanelEventHandler(self)

    def draw(self, screen: pygame.Surface, entity_data: dict, px: int, py: int, pad: int, font_h: int, panel_w: int) -> None:
        """Dibuja subtabs y grid de assets usando model y view."""
        # Dibujar subtabs y grid
        self.view.draw(screen, self.model, entity_data, px, py, pad, font_h, panel_w)
        
    def handle_event(self, event: pygame.event.Event) -> bool:
        """Delegación de eventos relacionados al grid."""
        return self.event_handler.handle(event)
