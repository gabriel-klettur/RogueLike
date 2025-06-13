import pygame
import math
import types
import pytest
from roguelike_game.ecs.systems.rendering.hitbox_debug_system import HitboxDebugSystem

class DummyWorld:
    def __init__(self):
        self.components = {'Position': {}, 'HitboxComponent': {}}

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

@pytest.fixture
def world():
    w = DummyWorld()
    w.components['Position'][1] = types.SimpleNamespace(x=10, y=10)
    w.components['HitboxComponent'][1] = types.SimpleNamespace(radius=5, direction=(1,0), arc_angle=math.pi/2)
    return w

@pytest.fixture
def camera():
    return DummyCam()

@pytest.fixture
def screen():
    return pygame.Surface((100, 100))

def test_circle_cache_built_and_reused(world, camera, screen):
    sys = HitboxDebugSystem(perf_log=None)
    assert sys.circle_surfs == {}
    sys.update(world, screen, camera)
    assert 5 in sys.circle_surfs
    first = sys.circle_surfs[5]
    sys.update(world, screen, camera)
    assert sys.circle_surfs[5] is first

def test_culling_offscreen(world, camera, screen):
    world.components['Position'][1] = types.SimpleNamespace(x=200, y=200)
    sys = HitboxDebugSystem(perf_log=None)
    sys.update(world, screen, camera)
    assert sys.circle_surfs == {}
