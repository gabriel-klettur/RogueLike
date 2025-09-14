"""
Controlador para la toolbar de Particulas.
"""


class ParticlesToolBarPanelController:
    """Controlador de la toolbar de Particulas."""
    def __init__(self, editor_controller, model, view, event_handler):
        """
        Args:
            editor_controller: Controlador principal del editor de particulas.
            model: Modelo del toolbar.
            view: Vista del toolbar.
            event_handler: Manejador de eventos del toolbar.
        """
        self.editor_controller = editor_controller
        self.model = model
        self.view = view
        self.event_handler = event_handler
        # Exponer referencias opcionales similares a otros controladores
        # self.title_controller = getattr(editor_controller, 'title_controller', None)
        # self.add_remove_controller = None

    # API requerida por ToolbarView para pintar seleccion (si se usara este controller directamente)
    def is_active(self, tool: str) -> bool:
        return getattr(self.model, 'active_tool', None) == tool

    def render(self, screen):
        """Delegar render al view."""
        if hasattr(self.view, 'render'):
            self.view.render(screen)

    def handle_event(self, event) -> bool:
        """Delegar evento al view (drag/hover) y luego al event handler."""
        if hasattr(self.view, 'handle_event') and self.view.handle_event(event):
            return True
        return self.event_handler.handle_event(event) if hasattr(self.event_handler, 'handle_event') else False
