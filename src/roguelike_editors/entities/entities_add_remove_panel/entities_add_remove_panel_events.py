class EntitiesAddRemovePanelEventHandler:
    """
    Manejador de eventos para el panel de añadir/eliminar entidades.
    """
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        """
        Procesa eventos de click y atajos (sin funcionalidad aún).
        """
        return False
