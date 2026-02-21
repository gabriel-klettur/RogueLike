class EntitiesTitleEventHandler:
    """
    Event handler for the Entities Title Panel.
    """
    def __init__(self, controller, model):
        """
        Args:
            controller: EntitiesTitleController instance.
            model: EntitiesTitleModel instance.
        """
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        """
        No events to handle for title panel.
        """
        return False
