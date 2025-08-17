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
        if not c.model.visible:
            return
        # 1) Title bar (always renders with its own font)
        try:
            title_rect = c.title_controller.render(screen)
        except Exception:
            title_rect = None
        # 2) Hint overlay (only if editor font is available); place below title
        try:
            if c.font:
                hint_y = (title_rect.bottom + 6) if title_rect else 10
                text = c.font.render("Spawner Editor (RMB drag to move)", True, (0, 200, 255))
                screen.blit(text, (10, hint_y))
        except Exception:
            pass
