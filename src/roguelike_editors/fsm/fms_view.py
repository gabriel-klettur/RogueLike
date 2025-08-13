import logging
from typing import Optional, Tuple

from .fms_controller import FMSController

logger = logging.getLogger(__name__)


class FMSView:
    """
    Vista mínima del editor FSM.
    - Se adjunta al controlador para recibir notificaciones de cambios.
    - Expone utilidades opcionales de UI (texto/overlay de estado).
    """

    def __init__(self, controller: Optional[FMSController] = None) -> None:
        self._controller = controller or FMSController.instance()
        self._controller.attach_view(self)
        self._last_state: bool = self._controller.is_debug_entities_enabled()

    # --- Callbacks desde el controlador ---
    def on_debug_toggle(self, enabled: bool) -> None:
        self._last_state = bool(enabled)
        try:
            logger.debug("FSMView: Entities Debug %s", "ON" if enabled else "OFF")
        except Exception:
            pass

    # --- API opcional de UI ---
    def get_status_text(self) -> str:
        return f"Entities Debug: {'ON' if self._last_state else 'OFF'}"

    def draw_status(self, surface, topleft: Tuple[int, int] = (8, 8)) -> None:
        """
        Dibuja un pequeño badge con el estado actual (opcional).
        Importa pygame localmente para evitar dependencias duras en importación.
        """
        try:
            import pygame
            x, y = topleft
            text = self.get_status_text()
            font = pygame.font.SysFont(None, 16)
            text_surf = font.render(text, True, (230, 230, 230))
            pad = 4
            rect = text_surf.get_rect(topleft=(x + pad, y + pad))
            bg_rect = rect.inflate(pad * 2, pad * 2)
            # Fondo semi-transparente
            badge = pygame.Surface(bg_rect.size, pygame.SRCALPHA)
            badge.fill((0, 0, 0, 140))
            surface.blit(badge, bg_rect.topleft)
            surface.blit(text_surf, rect.topleft)
        except Exception:
            # Si pygame o font no están disponibles, omitir silenciosamente
            pass
