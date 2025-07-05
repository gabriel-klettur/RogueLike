import pytest
import os
import pygame
import pytest
from types import SimpleNamespace
from roguelike_engine.input.events import handle_events
from roguelike_engine.input.keyboard import handle_keyboard
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector

@pytest.fixture(autouse=True)
def init_pygame():
    os.environ['SDL_VIDEODRIVER'] = 'dummy'
    pygame.display.init()
    pygame.font.init()
    pygame.display.set_mode((100,100))
    pygame.event.clear()
    yield
    pygame.display.quit()

class DummyState:
    def __init__(self):
        self.running = True
        self.item_editor_state = SimpleNamespace(visible=False)
        self.tile_editor_state = SimpleNamespace(active=False, picker_open=False, selected_tile=None, current_choice=None)

class DummyMenu:
    def __init__(self):
        self.input_config = SimpleNamespace(get_key=lambda k: (pygame.K_F6 if k=='pause' else pygame.K_F7 if k=='toggle_item_editor' else None))
        self.show_menu = False
    def handle_input(self, event): return None
    def execute_menu_option(self, result, state): pass

class DummyEditor:
    def __init__(self):
        self.editor_state = SimpleNamespace(active=False)
        self.handler = SimpleNamespace(handle=lambda cam, m: None)
    def toggle(self):
        if hasattr(self, 'toggled'):
            self.toggled = not self.toggled
        else:
            self.toggled = True

@pytest.fixture
def env():
    state = DummyState()
    camera = None
    clock = None
    menu = DummyMenu()
    map_manager = None
    entities = None
    tiles_editor = DummyEditor()
    buildings_editor = DummyEditor()
    map_editor = DummyEditor()
    return state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor

def test_engine_quit_closes_game(env):
    state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor = env
    pygame.event.post(pygame.event.Event(pygame.QUIT))
    handle_events(state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor)
    assert not state.running

def test_engine_toggle_item_editor(env):
    state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor = env
    # KEYDOWN F7 toggles item editor visibility
    event = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_F7)
    pygame.event.post(event)
    handle_events(state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor)
    assert state.item_editor_state.visible

def test_engine_toggle_tile_editor(env):
    state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor = env
    # KEYDOWN F8 toggles tile editor
    event = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_F8)
    pygame.event.post(event)
    handle_events(state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor)
    assert tiles_editor.editor_state.active

def test_engine_toggle_map_editor(env):
    state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor = env
    # KEYDOWN F11 toggles map editor
    event = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_F11)
    pygame.event.post(event)
    handle_events(state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor)
    assert getattr(map_editor, 'toggled', False)

from roguelike_editors.items.controller.editor_controller import ItemEditorController

@pytest.fixture
def controller():
    # minimal dummy items and assets and font
    items = {'a': SimpleNamespace(id='a', name='A', description='a', stackable=False)}
    assets = {'a': pygame.Surface((10,10))}
    font = pygame.font.SysFont(None, 12)
    ctrl = ItemEditorController(items, assets, font)
    return ctrl

def test_controller_toggle_item_editor(controller):
    ctrl = controller
    assert not ctrl.model.visible
    # simulate F7 key
    event = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_F7)
    ctrl.handle_event(event)
    assert ctrl.model.visible
    # toggle off
    event2 = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_F7)
    ctrl.handle_event(event2)
    assert not ctrl.model.visible
