import os
import sys
import pygame
import pytest

# Ensure absolute imports from src/ work without editable install
SRC_PATH = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir, "src"))
if SRC_PATH not in sys.path:
    sys.path.insert(0, SRC_PATH)

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
