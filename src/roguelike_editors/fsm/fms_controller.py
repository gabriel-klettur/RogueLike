import logging
from typing import Optional, Callable, List, TYPE_CHECKING

from .fms_model import FMSModel

if TYPE_CHECKING:
    from .fms_view import FMSView

logger = logging.getLogger(__name__)


class FMSController:
    """
    Controlador del editor FSM. Mantiene un modelo de estado y expone
    operaciones para alternar/establecer el debug de entidades.

    Ofrece un mecanismo simple de suscripción para que vistas (u otros
    interesados) reaccionen a cambios de estado.
    """

    _instance: Optional["FMSController"] = None

    def __init__(self, view: Optional["FMSView"] = None) -> None:
        self.model: FMSModel = FMSModel.from_config()
        self._subscribers: List[Callable[[FMSModel], None]] = []
        self._view = view

    @classmethod
    def instance(cls) -> "FMSController":
        if cls._instance is None:
            cls._instance = FMSController()
        return cls._instance

    def attach_view(self, view: "FMSView") -> None:
        self._view = view

    # --- API de estado ---
    def toggle_debug_entities(self) -> None:
        self.model.debug_entities_enabled = not self.model.debug_entities_enabled
        self.model.apply_to_config()
        logger.debug(
            " ENTITIES DEBUG %s",
            "activado" if self.model.debug_entities_enabled else "desactivado",
        )
        # Notificar vista y suscriptores
        if self._view:
            try:
                self._view.on_debug_toggle(self.model.debug_entities_enabled)
            except Exception:
                pass
        self._notify_subscribers()

    def set_debug_entities(self, enabled: bool) -> None:
        prev = self.model.debug_entities_enabled
        self.model.debug_entities_enabled = bool(enabled)
        self.model.apply_to_config()
        if prev != self.model.debug_entities_enabled:
            logger.debug(
                " ENTITIES DEBUG %s (set)",
                "activado" if self.model.debug_entities_enabled else "desactivado",
            )
            if self._view:
                try:
                    self._view.on_debug_toggle(self.model.debug_entities_enabled)
                except Exception:
                    pass
            self._notify_subscribers()

    def is_debug_entities_enabled(self) -> bool:
        return bool(self.model.debug_entities_enabled)

    # --- Subscripción ---
    def subscribe(self, callback: Callable[[FMSModel], None]) -> None:
        if callback not in self._subscribers:
            self._subscribers.append(callback)

    def unsubscribe(self, callback: Callable[[FMSModel], None]) -> None:
        if callback in self._subscribers:
            self._subscribers.remove(callback)

    def _notify_subscribers(self) -> None:
        for cb in list(self._subscribers):
            try:
                cb(self.model)
            except Exception:
                # Ignorar errores de callbacks de terceros
                pass
