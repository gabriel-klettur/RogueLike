from __future__ import annotations

import pygame


class SpawnerEditorView:
    """View responsible for rendering Spawner Editor overlays.

    Keeps drawing concerns separate from input/event logic.
    """
    def __init__(self, controller):
        self.controller = controller

    def render(self, screen: pygame.Surface) -> None:
        c = self.controller
        if not c.model.visible or not c.font:
            return
        # Hint overlay
        try:
            text = c.font.render("Spawner Editor (RMB drag to move)", True, (0, 200, 255))
            screen.blit(text, (10, 10))
        except Exception:
            pass
