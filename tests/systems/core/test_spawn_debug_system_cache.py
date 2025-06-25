import pygame
import pytest
from roguelike_game.ecs.systems.core.spawn_debug_system import SpawnDebugSystem
import roguelike_engine.config.config as config
from roguelike_engine.config.config_tiles import TILE_SIZE

class DummyWorld:
    def __init__(self):
        self.spawn_tiles = [(1,1,42)]

class DummyCam:
    def __init__(self):
        self.screen_width = 100
        self.screen_height = 100
        self.zoom = 1
        self.offset_x = 0
        self.offset_y = 0
    def apply(self, pos):
        return pos

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()

@pytest.fixture(autouse=True)
def enable_debug_entities(monkeypatch):
    monkeypatch.setattr(config, 'DEBUG_ENTITIES', True)
    monkeypatch.setattr(config, 'DEBUG_ENTITIES_FRAME_SKIP', 1)

@pytest.fixture
def world():
    return DummyWorld()

@pytest.fixture
def camera():
    return DummyCam()

@pytest.fixture
def screen():
    return pygame.Surface((100, 100))

def test_spawn_cache_and_culling(world, camera, screen):
    sys = SpawnDebugSystem(perf_log=None)
    # Primera llamada, cache vacío
    assert sys.fonts == {}
    assert sys.text_surfs == {}
    sys.update(world, screen, camera)
    # Después de dibujar, deberíamos tener cache
    size = int(TILE_SIZE * camera.zoom)
    font_size = max(8, size // 2)
    assert font_size in sys.fonts
    # La text_surfs key es (eid, font_size)
    assert (42, font_size) in sys.text_surfs
