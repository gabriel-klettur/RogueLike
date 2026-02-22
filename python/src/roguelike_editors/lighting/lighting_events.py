from __future__ import annotations

import pygame
from typing import Any


class LightingEditorEventHandler:
    """Thin event facade for the Lighting Editor.

    Keeps API symmetry with other editors (e.g., FSM/Particles) and allows
    future extension without changing call sites.
    """

    @staticmethod
    def handle_event(controller: Any, event: pygame.event.Event) -> None:
        if controller is None:
            return
        try:
            controller.handle_event(event)
        except Exception:
            # Never break the main loop due to optional editor UI
            pass
