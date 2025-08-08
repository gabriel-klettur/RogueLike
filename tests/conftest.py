import os
import pygame
import pytest

@pytest.fixture(scope="session", autouse=True)
def pygame_headless():
    os.environ.setdefault("SDL_AUDIODRIVER", "dummy")
    os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
    pygame.init()
    pygame.display.init()
    try:
        pygame.display.set_mode((1, 1))
    except pygame.error:
        # Retry without dummy driver
        os.environ.pop("SDL_VIDEODRIVER", None)
        try:
            pygame.display.quit()
        except Exception:
            pass
        pygame.display.init()
        pygame.display.set_mode((1, 1))
    yield
    try:
        pygame.display.quit()
    finally:
        pygame.quit()
