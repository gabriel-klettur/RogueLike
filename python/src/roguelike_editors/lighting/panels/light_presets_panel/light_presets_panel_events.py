from __future__ import annotations

import pygame
from typing import Any


class LightPresetsPanelEventHandler:
    """Thin event facade for the Light Presets panel.

    Keeps API symmetry with other panels and allows future extension
    without changing call sites.
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

