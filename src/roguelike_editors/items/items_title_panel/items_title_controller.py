from __future__ import annotations

from typing import Any
import pygame

from .items_title_view import ItemsTitleView


class ItemsTitleController:
    """Controller ligero para el título del Items Editor."""

    def __init__(self, state_model: Any) -> None:
        self.view = ItemsTitleView(self, state_model)

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        return self.view.render(screen)
