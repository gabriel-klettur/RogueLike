"""
Controlador para el sub-toolbar de añadir/eliminar Items.
"""


class ItemsAddRemovePanelController:
    """
    Controlador del panel de añadir/eliminar items.
    """
    def __init__(self, controller, model, view, event_handler):
        self.controller = controller
        self.model = model
        self.view = view
        self.event_handler = event_handler

    def render(self, screen):
        self.view.render(screen)

    def handle_event(self, event):
        if self.view.handle_event(event):
            return True
        return self.event_handler.handle_event(event)

    # API para ToolbarView (selección activa)
    def is_active(self, tool: str) -> bool:
        return getattr(self.model, 'active_tool', None) == tool

