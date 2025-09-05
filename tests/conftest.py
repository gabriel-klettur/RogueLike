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


# Ensure robustness if individual tests call pygame.quit() mid-suite.
# This re-initializes pygame subsystems as needed before each test.
@pytest.fixture(autouse=True)
def ensure_pygame_initialized():
    if not pygame.get_init():
        pygame.init()
    if not pygame.display.get_init():
        try:
            pygame.display.init()
            # Keep tiny window for headless
            pygame.display.set_mode((1, 1))
        except pygame.error:
            # Best-effort; session fixture already tried to set dummy drivers
            pass
    if not pygame.font.get_init():
        try:
            pygame.font.init()
        except pygame.error:
            # Some environments may still fail; tests that don't render fonts won't depend on it
            pass


# Avoid filesystem writes from tile overlay saving in tests
@pytest.fixture(autouse=True)
def patch_overlay_save_layers(monkeypatch):
    try:
        import roguelike_engine.map.model.overlay.overlay_manager as om
        monkeypatch.setattr(om, "save_layers", lambda *a, **k: None, raising=False)
    except Exception:
        # If the module cannot be imported in the current test, ignore
        pass
