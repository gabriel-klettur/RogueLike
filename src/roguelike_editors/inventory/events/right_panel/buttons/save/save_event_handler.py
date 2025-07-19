import pygame

class SaveEventHandler:
    """
    Event handler para flujo de guardado de inventario (Save Default/Active).
    """
    def __init__(self, controller):
        self.controller = controller
        self.view = controller.editor_controller.view
        self.model = controller.editor_controller.model

    def handle(self, event):
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            if getattr(self.view, 'save_rect', None) and self.view.save_rect.collidepoint(mx, my):
                if self.model.editing_side == 'default':
                    self.controller.save_default()
                else:
                    self.controller.save_active()
                return True
        return False
