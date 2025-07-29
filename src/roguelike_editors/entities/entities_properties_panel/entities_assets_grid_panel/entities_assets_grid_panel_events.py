import pygame

class AssetsGridPanelEventHandler:
    """Event handler for the assets grid panel."""
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view

    def handle(self, event: pygame.event.Event) -> bool:
        """Handle an event and return whether it was consumed."""
        if event.type == pygame.MOUSEMOTION:
            return self._handle_hover(event)
        elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            return self._handle_click(event)
        return False

    def _handle_hover(self, event: pygame.event.Event) -> bool:
        """Handle mouse motion to detect hover over asset cells."""
        if not hasattr(self.model, 'asset_cell_entries') or not self.model.asset_cell_entries:
            return False
            
        mx, my = event.pos
        hovered = None
        
        for rect, key in self.model.asset_cell_entries:
            if rect.collidepoint(mx, my):
                hovered = key
                break
                
        self.model.hovered_asset_cell = hovered
        return hovered is not None

    def _handle_click(self, event: pygame.event.Event) -> bool:
        """Handle mouse click to select an asset cell."""
        if not hasattr(self.model, 'asset_cell_entries') or not self.model.asset_cell_entries:
            return False
            
        mx, my = event.pos
        clicked = False
        for rect, key in self.model.asset_cell_entries:
            if rect.collidepoint(mx, my):
                self.model.selected_asset_cell = key  # Set selected asset cell
                clicked = True
                break
                
        return clicked
