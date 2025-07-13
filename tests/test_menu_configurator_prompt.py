import pytest
import pygame
from roguelike_ui.widgets.menu_configurator import MenuConfigurator

class DummyConfig:
    def __init__(self):
        self.bindings = {'action1': 'K_X'}
        self.set_called = None
        self.save_called = False
    def set_key(self, action, keyname):
        self.set_called = (action, keyname)
    def save(self):
        self.save_called = True

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def configurator(tmp_path, monkeypatch):
    config = DummyConfig()
    screen = pygame.Surface((200, 200))
    font = pygame.font.SysFont(None, 12)
    cfg = MenuConfigurator(config, screen, font)
    return cfg, config


def test_prompt_key_assignment(monkeypatch, configurator):
    cfg, config = configurator
    # Simulate one event: KEYDOWN with pygame.K_c
    events = [pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_c})]
    monkeypatch.setattr(pygame.event, 'get', lambda: events)
    # Stub display.flip to avoid Display mode not set error
    monkeypatch.setattr(pygame.display, 'flip', lambda: None)
    # Invoke prompt
    result = cfg._prompt_key('action1')
    # Should assign and save
    assert config.set_called == ('action1', 'K_C')
    assert config.save_called is True
    # Should return None
    assert result is None
