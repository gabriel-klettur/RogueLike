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
            for rect, tag in self.view.tab_rects:
                if rect.collidepoint(mx, my):
                    # Tabs secundarias para alternar lado de edición
                    if tag == 'show_default':
                        # Cambiar a edición de defaults
                        self.editor_controller.model.editing_side = 'default'
                        return True
                    if tag == 'show_active':
                        # Cambiar a edición de activos
                        self.editor_controller.model.editing_side = 'active'
                        return True
                    # Tabs normales de categoría
                    self.controller.change_category(tag)
                    return True
        return False
