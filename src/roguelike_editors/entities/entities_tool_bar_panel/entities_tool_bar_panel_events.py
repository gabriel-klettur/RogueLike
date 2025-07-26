"""
Manejador de eventos para la toolbar de entidades (stub).
"""

class EntitiesToolBarPanelEventHandler:
    """
    Maneja eventos de la toolbar de entidades.
    """
    def __init__(self, controller, model):
        """
        Args:
            controller: Instancia del controlador de toolbar.
            model: Instancia del modelo de toolbar.
        """
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        """
        Procesa eventos de click y atajos (sin funcionalidad aún).
        """
        return False