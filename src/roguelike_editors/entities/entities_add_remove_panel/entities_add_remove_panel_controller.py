class EntitiesAddRemovePanelController:
    """
    Controlador para el panel de añadir/eliminar entidades.
    """
    def __init__(self, controller, model, view, event_handler):
        self.controller = controller
        self.model = model
        self.view = view
        self.event_handler = event_handler

    def render(self, screen):
        """
        Renderiza el panel de añadir/eliminar entidades.
        """
        self.view.render(screen)

    def handle_event(self, event):
        """
        Maneja eventos, devuelve True si fue procesado.
        """
        if self.view.handle_event(event):
            return True
        return self.event_handler.handle_event(event)
