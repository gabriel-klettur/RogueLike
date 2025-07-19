import pygame
from roguelike_editors.inventory.controller.right_panel.item_selection_panel.item_selection_panel_controller import ItemSelectionPanelController

class ButtonEventHandler:
    """
    Handle confirm button clicks.
    """
    def __init__(self, grid_controller, controller: ItemSelectionPanelController, view):
        self.grid_controller = grid_controller
        self.controller = controller
        self.view = view
        self.model = controller.model

    def handle(self, event):
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            btn = getattr(self.view, 'add_button_rect', None)
            if btn and btn.collidepoint(mx, my):
                # update quantity from input
                try:
                    self.controller.set_quantity(self.view.text_input.text)
                except (ValueError, TypeError):
                    pass
                item, qty = self.controller.confirm()
                self.grid_controller.select_item(item)
                self.grid_controller.confirm_quantity(qty)
                self.view.text_input.active = False
                return True
        return False
