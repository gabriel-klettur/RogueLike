"""UI helpers for hot-reload feedback (loading bar/messages).

Kept tiny and defensive: failures should not break the reload flow.
"""
from __future__ import annotations

from typing import Any
import logging

logger = logging.getLogger(__name__)


def draw_loader(game: Any, frac: float, msg: str) -> None:
    """Draw a loading/progress indicator if available.

    Falls back to pumping pygame events to keep window responsive.
    """
    try:
        loader = getattr(game, "loader", None)
        if loader is None:
            from roguelike_engine.utils.loading_screen import LoadingScreen
            loader = LoadingScreen(game.screen)
            setattr(game, "loader", loader)
        loader.draw(max(0.0, min(1.0, float(frac))), str(msg))
    except Exception:
        try:
            import pygame  # type: ignore
            pygame.event.pump()
        except Exception:
            pass
