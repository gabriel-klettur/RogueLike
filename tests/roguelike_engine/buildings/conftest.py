import pytest
import pygame
from types import SimpleNamespace
from roguelike_engine.buildings.model_utils.image_ops import clear_building_image_cache


@pytest.fixture
def pygame_init():
    """Initialize pygame for tests and ensure quit after each test."""
    pygame.init()
    try:
        yield
    finally:
        pygame.quit()


@pytest.fixture
def patch_loader(monkeypatch):
    """Return a helper to patch the image loader used by BuildingModel.

    Usage:
        loader = patch_loader(size=(W, H))
    """
    def _apply(*, size=(64, 64)):
        clear_building_image_cache()
        def fake_loader(_path: str) -> pygame.Surface:
            return pygame.Surface(size, flags=pygame.SRCALPHA)
        monkeypatch.setattr(
            "roguelike_engine.buildings.building_model.load_image",
            fake_loader,
            raising=True,
        )
        return fake_loader
    return _apply


class _FakeCamera:
    def __init__(self, zoom: float = 1.0, offset=(0, 0)) -> None:
        self.zoom = float(zoom)
        self._ox, self._oy = int(offset[0]), int(offset[1])

    def scale(self, size: tuple[int, int]) -> tuple[int, int]:
        w, h = size
        return int(round(w * self.zoom)), int(round(h * self.zoom))

    def apply(self, pos: tuple[int, int]) -> tuple[int, int]:
        x, y = pos
        return x - self._ox, y - self._oy


@pytest.fixture
def fake_camera() -> _FakeCamera:
    """Provide a simple camera satisfying CameraProtocol."""
    return _FakeCamera(zoom=1.0, offset=(0, 0))


@pytest.fixture
def screen() -> pygame.Surface:
    return pygame.Surface((640, 480), flags=pygame.SRCALPHA)
