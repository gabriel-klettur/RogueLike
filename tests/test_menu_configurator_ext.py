import pytest
import pygame
import pygame_menu
import json
from roguelike_ui.widgets.menu_configurator import MenuConfigurator

@pytest.fixture
def setup_configurator(tmp_path, monkeypatch):
    # Create dummy config
    config_file = tmp_path / "cfg.json"
    config_data = {"bindings": {}}
    config_file.write_text(json.dumps(config_data))
    from roguelike_game.config.input_config import InputConfig
    input_config = InputConfig(str(config_file))
    screen = pygame.Surface((600, 400))
    font = pygame.font.SysFont("Arial", 18)
    return MenuConfigurator(input_config, screen, font)


def test_configure_theme_copies_and_sets_values(setup_configurator):
    configurator = setup_configurator
    theme = configurator._configure_theme()
    # Base is a THEME_DARK copy, title/widget sizes overridden
    assert theme.title_font_size == 24
    assert theme.widget_font_size == 18
    # Original THEME_DARK remains unchanged
    from pygame_menu.themes import THEME_DARK
    assert THEME_DARK.title_font_size != theme.title_font_size or THEME_DARK.widget_font_size != theme.widget_font_size


def test_calculate_rows_various_counts(setup_configurator):
    configurator = setup_configurator
    # No bindings: override to empty
    configurator.config.bindings = {}
    # No bindings
    assert configurator._calculate_rows() == 1  # (0+2)/2 = 1
    # 1 binding
    configurator.config.bindings = {'a': 'K_a'}
    assert configurator._calculate_rows() == 2  # (1+2)/2 = 1.5 -> 2
    # 3 bindings
    configurator.config.bindings = {'a': 'K_a', 'b': 'K_b', 'c': 'K_c'}
    assert configurator._calculate_rows() == 3  # (3+2)/2 = 2.5 -> 3
    # 4 bindings
    configurator.config.bindings = {'a': '1','b':'2','c':'3','d':'4'}
    assert configurator._calculate_rows() == 3  # (4+2)/2 = 3


def test_prompt_key_escape_cancels(monkeypatch, setup_configurator):
    configurator = setup_configurator
    # Simulate one ESC event
    events = [pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_ESCAPE})]
    monkeypatch.setattr(pygame, 'event', type('E', (), {'get': staticmethod(lambda: events)}))
    # Stub display.flip to avoid video error
    monkeypatch.setattr(pygame.display, 'flip', lambda: None)
    # No exception, returns None
    assert configurator._prompt_key('any') is None
