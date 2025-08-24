import logging

logger = logging.getLogger(__name__)


class ViewLayersEvents:
    """
    Eventos para el dropdown de 'view_layers'.
    Encapsula la detección de clics sobre opciones y aplica selecciones en el modelo.
    """
    def __init__(self, controller, model=None):
        self.controller = controller
        self.model = model or getattr(controller, "model", None)

    def handle_dropdown_click(self, mouse_pos) -> bool:
        """Detecta qué opción del dropdown fue clicada y aplica la selección."""
        if not self.controller.is_open():
            return False
        rects = getattr(self.model, "option_rects", {})
        for key, rect in rects.items():
            if rect and rect.collidepoint(mouse_pos):
                self.model.apply_selection(key)
                logger.debug("[ViewLayersEvents] click on option: %s", key)
                return True
        return False
