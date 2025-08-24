"""
Controlador para la toolbar de Items.
"""

class ItemsToolBarPanelController:
    """
    Controlador de la toolbar de Items.
    """
    def __init__(self, editor_controller, model, view, event_handler):
        """
        Args:
            editor_controller: Controlador principal del Inventory Editor.
            model: Modelo de la toolbar.
            view: Vista de la toolbar.
            event_handler: Manejador de eventos de la toolbar.
        """
        self.editor_controller = editor_controller
        self.model = model
        self.view = view
        self.event_handler = event_handler
        # Exponer title_controller para cálculo de posición en la vista
        self.title_controller = getattr(editor_controller, 'title_controller', None)
        # Se inyectará desde el InventoryEditorController tras crear ambos toolbars
        self.add_remove_controller = None

    # API requerido por ToolbarView para pintar selección
    def is_active(self, tool: str) -> bool:
        return getattr(self.model, 'active_tool', None) == tool

    def render(self, screen):
        """Delegar render al view."""
        if hasattr(self.view, 'render'):
            self.view.render(screen)

    def handle_event(self, event):
        """Delegar evento al view y al manejador."""
        if hasattr(self.view, 'handle_event') and self.view.handle_event(event):
            return True
        return self.event_handler.handle_event(event) if hasattr(self.event_handler, 'handle_event') else False

