import pygame
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_model import AssetsGridPanelModel

class AssetsGridPanelEventHandler:
    """Manejador de eventos para el panel de cuadrícula de assets."""
    def __init__(self, controller):
        self.controller = controller
        self.model: AssetsGridPanelModel = controller.model
        self.parent = controller.parent_model

    def handle(self, event: pygame.event.Event) -> bool:
        # Solo manejar si tab activo es 'assets'
        if getattr(self.parent, 'active_tab', None) != 'assets':
            return False
        # Verificar que panel esté interactivo
        if not hasattr(self.parent, 'panel_rect') or not self.parent.panel_rect.collidepoint(getattr(event, 'pos', (-1, -1))):
            return False
        # Hover celda
        if event.type == pygame.MOUSEMOTION:
            mx, my = event.pos
            hovered = None
            for rect, key in self.model.asset_cell_entries:
                if rect.collidepoint(mx, my):
                    hovered = key
                    break
            self.model.hovered_asset_cell = hovered
            return True
        # Click en subtabs
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            for label, rect in self.parent.asset_tab_rects.items():
                if rect.collidepoint(mx, my):
                    self.model.active_asset_tab = label
                    self.parent.active_asset_tab = label
                    # Reset selection
                    self.model.hovered_asset_cell = None
                    self.model.selected_asset_cell = None
                    return True
            # Click en celdas
            for rect, key in self.model.asset_cell_entries:
                if rect.collidepoint(mx, my):
                    self.model.selected_asset_cell = key
                    return True
        return False
