import pygame
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_controller import ItemSelectionPanelController

class TittleEventHandler:
    """
    Handle panel close and dragging via header.
    """
    def __init__(self, controller: ItemSelectionPanelController, view):
        self.controller = controller
        self.view = view
        self.model = controller.model

    def handle(self, event):
        # Close panel if click outside
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            if not self.view.panel_rect.collidepoint(mx, my) and not self.view.header_rect.collidepoint(mx, my):
                self.controller.close()
                return True
        # Start drag on header
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if self.view.header_rect.collidepoint(event.pos):
                self.model.dragging = True
                self.model.drag_start_pos = pygame.Vector2(event.pos) - self.model.drag_offset
                return True
        # Drag motion
        if event.type == pygame.MOUSEMOTION and self.model.dragging:
            self.model.drag_offset = pygame.Vector2(event.pos) - self.model.drag_start_pos
            return True
        # End drag
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and self.model.dragging:
            self.model.dragging = False
            return True
        return False
