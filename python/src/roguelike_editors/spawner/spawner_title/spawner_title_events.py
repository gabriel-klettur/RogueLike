class SpawnerTitleEventHandler:
    """
    Event handler para el panel de título del Spawner Editor (sin eventos por ahora).
    """
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        return False
