import pygame

class TabsEventHandler:
    """
    Event handler para flujo de cambiar tabs 'Show Default' y 'Show Active' en panel derecho.
    """
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.editor_controller.model
        self.view = controller.editor_controller.view

    def handle(self, event):
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            # Show Default
            if getattr(self.view, 'show_default_rect', None) and self.view.show_default_rect.collidepoint(mx, my):
                self.model.editing_side = 'default'
                return True
            # Show Active
            if getattr(self.view, 'show_active_rect', None) and self.view.show_active_rect.collidepoint(mx, my):
                self.model.editing_side = 'active'
                return True
        return False
