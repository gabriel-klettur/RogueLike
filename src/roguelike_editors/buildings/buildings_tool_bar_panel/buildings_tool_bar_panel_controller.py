"""
Controlador para la toolbar de Buildings.
"""


class BuildingsToolBarPanelController:
    """
    Controlador de la toolbar de Buildings.
    """
    def __init__(self, editor_manager, model, view, event_handler):
        """
        Args:
            editor_manager: Instancia de BuildingEditorManager para acceder a controller/view/state.
            model: Modelo de la toolbar.
            view: Vista de la toolbar.
            event_handler: Manejador de eventos de la toolbar.
        """
        self.editor_manager = editor_manager
        # Accesos directos útiles para la vista/eventos
        self.editor_controller = getattr(editor_manager, 'controller', None)
        self.editor_view = getattr(editor_manager, 'view', None)
        self.editor_state = getattr(editor_manager, 'editor_state', None)

        self.model = model
        self.view = view
        self.event_handler = event_handler

    # API requerido por ToolbarView para pintar selección
    def is_active(self, tool: str) -> bool:
        return getattr(self.model, 'active_tool', None) == tool

    def render(self, screen):
        if hasattr(self.view, 'render'):
            self.view.render(screen)

    def handle_event(self, event):
        # La vista (ToolbarView) puede consumir eventos de hover/click
        if hasattr(self.view, 'handle_event') and self.view.handle_event(event):
            return True
        # Delegar al manejador específico
        return self.event_handler.handle_event(event) if hasattr(self.event_handler, 'handle_event') else False

