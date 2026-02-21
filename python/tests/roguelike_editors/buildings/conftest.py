import os
import sys
import pathlib

# Ensure headless pygame for CI/automation
os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

# Make project src importable: add <project_root>/src to sys.path
PROJECT_ROOT = pathlib.Path(__file__).resolve().parents[3]
SRC_PATH = PROJECT_ROOT / "src"
if str(SRC_PATH) not in sys.path:
    sys.path.insert(0, str(SRC_PATH))

import pytest
import pygame


@pytest.fixture(scope="session", autouse=True)
def pygame_context():
    """Initialize and quit pygame in headless mode for the entire test session."""
    pygame.init()
    try:
        yield
    finally:
        pygame.quit()


class FakeCamera:
    def __init__(self, zoom: float = 1.0, offset_x: float = 0.0, offset_y: float = 0.0):
        self.zoom = zoom
        self.offset_x = offset_x
        self.offset_y = offset_y

    def apply(self, pos):
        x, y = pos
        return (int((x - self.offset_x) * self.zoom), int((y - self.offset_y) * self.zoom))

    def scale(self, size):
        w, h = size
        return (int(w * self.zoom), int(h * self.zoom))


@pytest.fixture()
def camera():
    return FakeCamera(zoom=1.0, offset_x=0, offset_y=0)


@pytest.fixture()
def surface_factory():
    def _make_surface(w=64, h=64, color=(0, 0, 0, 0)):
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        surf.fill(color)
        return surf
    return _make_surface


@pytest.fixture()
def spy_save(monkeypatch):
    """Spy on save_buildings_to_json without touching disk. Returns a call list."""
    try:
        from roguelike_editors.buildings.utils import save_buildings_to_json as save_mod
    except Exception:  # pragma: no cover - import errors will surface in import tests
        pytest.skip("save_buildings_to_json module not importable yet")

    calls = []

    def _fake_save(*args, **kwargs):
        calls.append((args, kwargs))
        return True

    monkeypatch.setattr(save_mod, "save_buildings_to_json", _fake_save, raising=True)
    return calls
