import os
import pygame
import pytest
from collections import defaultdict
from types import SimpleNamespace

from roguelike_game.managers.core.game import Game
from roguelike_engine.input.events import handle_events as engine_handle_events

@pytest.fixture(autouse=True)
def setup_pygame():
    os.environ['SDL_VIDEODRIVER'] = 'dummy'
    pygame.display.init()
    pygame.font.init()
    pygame.display.set_mode((10,10))
    pygame.event.clear()
    yield
    pygame.display.quit()

@pytest.fixture
def game():
    # create Game instance without running initializer
    g = object.__new__(Game)
    # minimal attributes for handle_events
    g.state = SimpleNamespace(running=True)
    g.state.item_editor_state = SimpleNamespace(visible=False)
    # ensure game.item_editor.model references same state
    g.item_editor = SimpleNamespace(model=g.state.item_editor_state, handle_event=lambda ev: setattr(g, 'handled_item_event', ev))
    # tiles editor stub
    g.tiles_editor = SimpleNamespace(editor_state=SimpleNamespace(active=False), handle=lambda cam, m: setattr(g, 'handled_tiles', True))
    g.buildings_editor = SimpleNamespace(editor_state=SimpleNamespace(active=False))
    g.map_editor = SimpleNamespace(editor_state=SimpleNamespace(active=False), toggle=lambda : setattr(g, 'toggled_map', True))
    # other deps
    g.camera = None
    g.map = None
    # menu uses get_key for toggle_item_editor
    g.menu = SimpleNamespace(input_config=SimpleNamespace(get_key=lambda k: (pygame.K_F7 if k=='toggle_item_editor' else pygame.K_F8 if k=='toggle_tile_editor' else pygame.K_F11 if k=='toggle_map_editor' else None)), show_menu=False, handle_input=lambda ev: None, execute_menu_option=lambda res, st: None)
    g.buildings = None
    g.renderer = SimpleNamespace(debug_overlay=None)
    g.clock = None
    g.perf_log = defaultdict(list)  # required by benchmark decorator
    return g

def test_quit_always_closes(game):
    pygame.event.post(pygame.event.Event(pygame.QUIT))
    # engine_handle_events only used when no editor open
    game.handle_events()
    assert not game.state.running

@pytest.mark.parametrize('key, state_attr, expected', [
    (pygame.K_F7, 'state.item_editor_state.visible', True),
    (pygame.K_F8, 'tiles_editor.editor_state.active', True),
    (pygame.K_F11, 'toggled_map', True),
])
def test_toggle_editors(game, key, state_attr, expected):
    # post key event
    pygame.event.post(pygame.event.Event(pygame.KEYDOWN, key=key))
    game.handle_events()
    # verify attribute path
    parts = state_attr.split('.')
    obj = game
    for part in parts[:-1]:
        obj = getattr(obj, part)
    assert getattr(obj, parts[-1]) == expected
