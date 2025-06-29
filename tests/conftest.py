# Path: tests/conftest.py
import sys, os
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "src")))
import pytest
import types
try:
    import pygame
except ModuleNotFoundError:
    pygame = types.SimpleNamespace(
        init=lambda: None,
        Surface=lambda *args, **kwargs: None,
        Rect=lambda *args, **kwargs: None,
        mouse=types.SimpleNamespace(get_pos=lambda: (0,0), get_pressed=lambda: (False, False, False)),
    )
    sys.modules['pygame'] = pygame
from roguelike_game.ecs.core.manager import ECSWorld

class DummyMapManager:
    def __init__(self):
        self.solid_tiles = []

class DummyBuilding:
    def __init__(self):
        self.collision_tiles = []

class DummyCamera:
    screen_width = 800
    screen_height = 600
    def apply(self, pos):
        return pos

@pytest.fixture(scope="session", autouse=True)
def init_pygame():
    pygame.init()

@pytest.fixture
def world():
    dummy_map = DummyMapManager()
    buildings = []
    return ECSWorld(screen=None, map_manager=dummy_map, buildings=buildings, perf_log=None)

@pytest.fixture
def camera():
    return DummyCamera()

@pytest.fixture
def screen():
    return pygame.Surface((800, 600))