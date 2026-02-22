import os
import pygame

from roguelike_engine.utils.loader import load_image


def test_load_image_png_error_returns_placeholder(monkeypatch):
    # Force file to "exist" to bypass FileNotFoundError branch
    monkeypatch.setattr(os.path, "isfile", lambda p: True)
    # Make pygame.image.load fail to trigger error handling branch
    def _boom(path):
        raise pygame.error("corrupt png")
    monkeypatch.setattr(pygame.image, "load", _boom)

    scale = (16, 12)
    surf = load_image("assets/tiles/fake.png", scale=scale)
    assert isinstance(surf, pygame.Surface)
    assert surf.get_size() == scale
