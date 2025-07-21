class TilesViewPanelEventHandler:
    """Event handler for the Tiles View Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs):
        # No interactive events for view panel
        return False
