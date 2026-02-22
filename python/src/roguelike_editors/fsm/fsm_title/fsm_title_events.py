class FsmTitleEventHandler:
    """
    Event handler para el panel de título del editor FSM.
    """
    def __init__(self, controller, model):
        """
        Args:
            controller: FsmTitleController instance.
            model: FsmTitleModel instance.
        """
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        """
        No hay eventos que manejar para el título por ahora.
        """
        return False
