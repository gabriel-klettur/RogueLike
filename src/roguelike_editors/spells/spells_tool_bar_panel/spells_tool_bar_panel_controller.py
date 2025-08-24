"""
Controlador para la toolbar de Spells.
"""


class SpellsToolBarPanelController:
    """Controlador de la toolbar de Spells."""
    def __init__(self, editor_controller, model, view, event_handler):
        self.editor_controller = editor_controller
        self.model = model
        self.view = view
        self.event_handler = event_handler
        # Para posicionamiento bajo el título
        self.title_controller = getattr(editor_controller, 'title_controller', None)
        # Exponer picker/editor para fallback de posicionamiento
        self.picker_controller = editor_controller
        # Referencia opcional al panel Add/Remove
        self.add_remove_controller = None

    def is_active(self, tool: str) -> bool:
        return getattr(self.model, 'active_tool', None) == tool

    def render(self, screen):
        if hasattr(self.view, 'render'):
            self.view.render(screen)

    def handle_event(self, event):
        if hasattr(self.view, 'handle_event') and self.view.handle_event(event):
            return True
        return self.event_handler.handle_event(event) if hasattr(self.event_handler, 'handle_event') else False

