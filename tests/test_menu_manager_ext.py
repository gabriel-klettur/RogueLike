import pytest
import pygame
from roguelike_game.managers.menu.controller.menu_manager import MenuManager

class DummyHandler:
    def __init__(self):
        self.input_events = []
        self.get_opts = ['O1', 'O2']
        self.selected = 1
        self.exec_called = None
    def handle_input(self, event):
        self.input_events.append(event)
        return 'ret'
    def get_options(self):
        return self.get_opts
    def execute_option(self, opt):
        self.exec_called = opt

class DummyRenderer:
    def __init__(self):
        self.draw_calls = []
    def draw(self, screen, selected, options):
        self.draw_calls.append((screen, selected, options))
        return 'rect'

class DummyConfigurator:
    pass

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def manager(monkeypatch):
    # Create a MenuManager but override handler and renderer
    state = type('S', (), {'running': True})
    screen = pygame.Surface((100, 100))
    input_config = None
    mm = MenuManager(state, screen, input_config)
    # Replace handler and renderer
    dummy_handler = DummyHandler()
    dummy_renderer = DummyRenderer()
    mm.handler = dummy_handler
    mm.renderer = dummy_renderer
    return mm, dummy_handler, dummy_renderer


def test_handle_input_delegation(manager):
    mm, handler, _ = manager
    evt = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_a)
    res = mm.handle_input(evt)
    assert res == 'ret'
    assert handler.input_events == [evt]


def test_draw_delegation(manager):
    mm, handler, renderer = manager
    screen = pygame.Surface((200, 200))
    # handler.get_options and handler.selected used
    handler.get_opts = ['X', 'Y']
    handler.selected = 0
    res = mm.draw(screen)
    assert res == 'rect'
    assert renderer.draw_calls == [(screen, 0, ['X', 'Y'])]


def test_execute_menu_option_continue(manager):
    mm, _, _ = manager
    mm.show_menu = True
    mm.execute_menu_option('Continuar', None)
    assert mm.show_menu is False


def test_execute_menu_option_delegates(manager):
    mm, handler, _ = manager
    mm.show_menu = True
    mm.execute_menu_option('OtherOption', None)
    assert handler.exec_called == 'OtherOption'
