"""
Particles Add/Remove panel controller.
"""


class ParticlesAddRemovePanelController:
    def __init__(self, editor_controller, model, view, event_handler):
        self.editor_controller = editor_controller
        self.model = model
        self.view = view
        self.event_handler = event_handler

    def is_active(self, tool: str) -> bool:
        return getattr(self.model, 'active_tool', None) == tool

    def render(self, screen):
        if hasattr(self.view, 'render'):
            self.view.render(screen)

    def handle_event(self, event):
        if hasattr(self.view, 'handle_event') and self.view.handle_event(event):
            return True
        return self.event_handler.handle_event(event) if hasattr(self.event_handler, 'handle_event') else False
