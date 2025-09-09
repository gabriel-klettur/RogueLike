from __future__ import annotations

import pygame
from pygame import Surface, Rect

from .chat_overlay_controller import ChatOverlayController


class ChatOverlayView:
    def __init__(self, controller: ChatOverlayController) -> None:
        self.controller = controller

    def render(self, surface: Surface, rect: Rect) -> None:
        self.controller.draw(surface, rect)
