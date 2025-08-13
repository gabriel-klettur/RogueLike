"""FSM editor event spy: only handles input and delegates to controller."""

from .fms_controller import FMSController


class FMSEventSpy:
    """
    Punto de entrada para el manejo de eventos relacionados a F12.
    Otros módulos deben delegar aquí en vez de tocar config directamente.
    """

    @staticmethod
    def toggle_debug() -> None:
        FMSController.instance().toggle_debug_entities()

    @staticmethod
    def handle_event(event) -> bool:
        """
        Si el evento corresponde a F12, alterna el debug y devuelve True (consumido).
        De lo contrario devuelve False.
        """
        try:
            import pygame  # import local para evitar dependencia dura al importar sin pygame
            if event.type == pygame.KEYDOWN and event.key == pygame.K_F12:
                FMSEventSpy.toggle_debug()
                return True
        except Exception:
            # Si pygame no está disponible o falla, no consumir el evento
            pass
        return False
