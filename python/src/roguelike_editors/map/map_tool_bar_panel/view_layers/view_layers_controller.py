import logging
from .view_layers_model import ViewLayersModel
from .view_layers_events import ViewLayersEvents
from .view_layers_view import ViewLayersView

logger = logging.getLogger(__name__)


class ViewLayersController:
    """
    Controller para el botón 'view_layers'.
    - Alterna la apertura del dropdown de visibilidad de capas.
    - Orquesta el rendering y el manejo de eventos del dropdown.
    - Encapsula la mutación de estado en el modelo.
    """
    def __init__(self, *, editor_state, toolbar_controller=None):
        self.editor = editor_state
        self.toolbar = toolbar_controller  # MapToolBarPanelController

        self.model = ViewLayersModel(editor_state)
        self.events = ViewLayersEvents(self, self.model)
        self.view = ViewLayersView(self, self.model)

    # API para el toolbar
    def toggle(self) -> bool:
        """Alterna la apertura del dropdown."""
        return self.model.toggle_open()

    def is_open(self) -> bool:
        return bool(getattr(self.editor, "layers_view_open", False))
