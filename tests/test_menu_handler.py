import pytest
import pygame
from roguelike_game.managers.menu.controller.menu_handler import MenuHandler

class DummyConfigurator:
    def __init__(self):
        self.called = False
    def configure(self):
        self.called = True

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def state():
    return type('S', (), {'mode':'local', 'running': True})

@pytest.fixture
def handler(state):
    input_config = None
    configurator = DummyConfigurator()
    return MenuHandler(state, input_config, configurator)


def test_get_options_local(state, handler):
    state.mode = 'local'
    opts = handler.get_options()
    assert opts == ['Continuar', 'Modo multijugador', 'Configurar Botones', 'Salir']


def test_get_options_online(state, handler):
    state.mode = 'online'
    opts = handler.get_options()
    assert opts[1] == 'Modo local'


def test_handle_input_up_down_and_return(state, handler):
    # Test UP wraps around
    evt_up = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_UP)
    res = handler.handle_input(evt_up)
    assert handler.selected == 3
    assert res is None
    # Test DOWN
    evt_down = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_DOWN)
    res = handler.handle_input(evt_down)
    assert handler.selected == 0
    # Test RETURN
    handler.selected = 2
    evt_ret = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_RETURN)
    res = handler.handle_input(evt_ret)
    assert res == handler.get_options()[2]


def test_execute_option_exit(state, handler):
    handler.execute_option('Salir')
    assert state.running is False


def test_execute_option_configure(monkeypatch, handler):
    # stub clear
    cleared = []
    monkeypatch.setattr(pygame.event, 'clear', lambda t: cleared.append(t))
    handler.execute_option('Configurar Botones')
    assert handler.configurator.called
    assert cleared == [pygame.KEYDOWN]


def test_execute_option_toggle(state, handler, capsys):
    # Toggle to online
    state.mode = 'local'
    handler.execute_option('Modo multijugador')
    assert state.mode == 'online'
    # Toggle back to local
    state.mode = 'online'
    handler.execute_option('Modo local')
    assert state.mode == 'local'
