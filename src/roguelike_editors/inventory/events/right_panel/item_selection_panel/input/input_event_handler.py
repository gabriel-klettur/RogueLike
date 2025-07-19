import pygame
from roguelike_editors.inventory.controller.right_panel.item_selection_panel.item_selection_panel_controller import ItemSelectionPanelController

class InputEventHandler:
    """
    Handle clicks and text input for quantity field.
    """
    def __init__(self, controller: ItemSelectionPanelController, view):
        self.controller = controller
        self.view = view
        self.model = controller.model

    def handle(self, event):
        # Activate quantity input on click
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if hasattr(self.view, 'input_rect') and self.view.input_rect.collidepoint(event.pos):
                self.view.text_input.activate(initial_text=str(self.model.quantity), select_all=True)
                return True
        # Handle text input events
        if self.view.text_input.handle_event(event):
            self.controller.set_quantity(self.view.text_input.text)
            return True
        return False
