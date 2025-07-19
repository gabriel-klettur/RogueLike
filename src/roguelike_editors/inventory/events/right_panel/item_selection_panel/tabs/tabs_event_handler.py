import pygame
from roguelike_editors.inventory.controller.right_panel.item_selection_panel.item_selection_panel_controller import ItemSelectionPanelController

class TabsEventHandler:
    """
    Handle tab switching clicks.
    """
    def __init__(self, controller: ItemSelectionPanelController, view):
        self.controller = controller
        self.view = view
        self.model = controller.model

    def handle(self, event):
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            for rect, label in zip(getattr(self.view, 'tab_rects', []), ['default', 'ground']):
                if rect.collidepoint(mx, my):
                    self.controller.change_tab(label)
                    self.view.scroll_panel.scroll_offset = 0
                    return True
        return False
