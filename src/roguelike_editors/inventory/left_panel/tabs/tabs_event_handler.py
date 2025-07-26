import pygame


class TabsEventHandler:
    """
    Manejador de eventos para las tabs del panel izquierdo.
    """
    def __init__(self, editor_controller, controller, view, model):
        self.editor_controller = editor_controller
        self.controller = controller
        self.view = view
        self.model = model

    def handle(self, event):
        """
        Maneja clicks en las tabs para cambiar categoría.
        """
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            for rect, cat in self.view.tab_rects:
                if rect.collidepoint(mx, my):
                    self.controller.change_category(cat)
                    return True
        return False
