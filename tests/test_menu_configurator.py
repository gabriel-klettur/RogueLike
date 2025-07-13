import pygame
import pytest
import pygame_menu
from roguelike_ui.widgets.menu_configurator import MenuConfigurator
from roguelike_game.config.input_config import InputConfig

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    screen = pygame.display.set_mode((800, 600))
    font = pygame.font.SysFont("Arial", 18)
    yield screen, font
    pygame.quit()


def test_make_binding_callback_sets_refresh_and_disables(init_pygame, tmp_path, monkeypatch):
    screen, font = init_pygame
    # Prepare a dummy config
    config_file = tmp_path / "bindings.json"
    config_data = {"bindings": {"test_action": "K_a"}}
    import json
    with open(config_file, 'w') as f:
        json.dump(config_data, f)
    input_config = InputConfig(str(config_file))
    # Override save to track calls
    calls = {'saved': False}
    def fake_save():
        calls['saved'] = True
    input_config.save = fake_save

    configurator = MenuConfigurator(input_config, screen, font)
    # Build menu and a dummy button to attach
    menu = pygame_menu.Menu('Test', 400, 300, theme=configurator._configure_theme())
    callback = configurator._make_binding_callback(menu, 'test_action')
    # Monkeypatch prompt_key to simulate user pressing new key
    def fake_prompt_key(action):
        configurator.config.set_key(action, 'K_b')
        configurator.config.save()
    monkeypatch.setattr(configurator, '_prompt_key', fake_prompt_key)

    # Before callback
    assert not calls['saved']
    callback()
    # After callback, config binding changed, save called, menu disabled, refresh flagged
    assert input_config.bindings['test_action'] == 'K_b'
    assert calls['saved']
    assert not menu.is_enabled()
    assert configurator._needs_refresh

