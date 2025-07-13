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
    # Prepare dummy config and configurator
    config = DummyConfig()
    screen = pygame.Surface((100, 100))
    font = pygame.font.SysFont(None, 12)
    cfg = MenuConfigurator(config, screen, font)
    return cfg, config


def test_make_binding_callback(monkeypatch, configurator):
    cfg, config = configurator
    # Stub prompt to avoid blocking
    called = []
    monkeypatch.setattr(cfg, '_prompt_key', lambda action: called.append(action))
    class DummyMenu:
        def __init__(self): self.disabled = False
        def disable(self): self.disabled = True
    menu = DummyMenu()
    # Before callback, needs_refresh False
    assert not cfg._needs_refresh
    cb = cfg._make_binding_callback(menu, 'action1')
    cb()
    # After callback
    assert cfg._needs_refresh
    assert called == ['action1']
    assert menu.disabled

