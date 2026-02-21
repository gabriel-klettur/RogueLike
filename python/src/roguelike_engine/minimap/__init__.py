from __future__ import annotations

import pygame
from typing import Tuple, Iterable, Optional

from .minimap_model import MinimapModel
from .minimap_view import MinimapView
from .minimap_controller import MinimapController
from .minimap_events import MinimapEvents

__all__ = [
    "Minimap",
    "MinimapModel",
    "MinimapView",
    "MinimapController",
    "MinimapEvents",
]


class Minimap:
    """
    Fachada del Minimap en arquitectura MVC+E.

    Conserva la API pública existente:
      - update(player_pos, tiles, buildings, world)
      - render(screen) -> pygame.Rect
      - get_rect(screen) -> pygame.Rect
      - handle_event(event, screen) -> bool
    """

    def __init__(self) -> None:
        self.model = MinimapModel()
        self.view = MinimapView()
        self.controller = MinimapController()
        self.events = MinimapEvents()

    # API pública
    def update(
        self,
        player_pos: Tuple[float, float],
        tiles: Iterable[object],
        buildings: Optional[Iterable] = None,
        world: Optional[object] = None,
    ) -> None:
        self.controller.update(self.model, player_pos, tiles, buildings, world)

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        return self.view.render(screen, self.model)

    def get_rect(self, screen: pygame.Surface) -> pygame.Rect:
        return self.view.get_rect(screen, self.model)

    def handle_event(self, event: pygame.event.Event, screen: pygame.Surface) -> bool:
        return self.events.handle_event(self.model, event, screen)
