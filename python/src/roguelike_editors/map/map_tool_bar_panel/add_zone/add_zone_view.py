class AddZoneView:
    """
    Minimal view stub for the Add Zone tool.
    The confirmation dialog is rendered by `MapEditorView._draw_confirmation_dialogs`,
    so this view is currently a placeholder for future overlays/affordances.
    """

    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def render(self, screen):
        # No-op for now
        return None
