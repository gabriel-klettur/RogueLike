import pygame
import roguelike_engine.config.config as config
import types
import pytest
from roguelike_game.ecs.systems.rendering.fsm.states_debug_render_system import StatesDebugRenderSystem

class DummyWorld:
    def __init__(self):
        self.components = {'NPCState': {}, 'Position': {}, 'Sprite': {}, 'Scale': {}}
    def get_entities_with(self, *comps):
        return [7]

class DummyCmp:
    def __init__(self, name):
        # Create a dynamic state class with the given name
        StateCls = type(name, (), {})
        self.fsm = types.SimpleNamespace(current_state=StateCls())
    @property
    def image(self):
        surf = pygame.Surface((10,10))
        return surf

class DummyCam:
    def __init__(self):
        self.screen_width = 50
        self.screen_height = 50
        self.offset_x = 0
        self.offset_y = 0
        self.zoom = 1
    def apply(self, pos):
        return pos

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    # Ensure debug mode is active for caching
    config.DEBUG_ENTITIES = True

@pytest.fixture
def world():
    w = DummyWorld()
    w.components['NPCState'][7] = DummyCmp('IdleState')
    w.components['Position'][7] = types.SimpleNamespace(x=5, y=5)
    w.components['Sprite'][7] = types.SimpleNamespace(image=pygame.Surface((5,5)))
    return w

@pytest.fixture
def camera():
    return DummyCam()

@pytest.fixture
def screen():
    return pygame.Surface((50, 50))

def test_label_cache_and_culling(world, camera, screen):
    sys = StatesDebugRenderSystem(perf_log=None)
    # Cache vacío inicialmente
    assert sys.text_cache == {}
    # update genera cache
    sys.update(world, screen, camera)
    assert 'IdleState' in sys.text_cache
    first = sys.text_cache['IdleState']
    # Si volvemos a llamar, no se crea de nuevo
    sys.update(world, screen, camera)
    assert sys.text_cache['IdleState'] is first
