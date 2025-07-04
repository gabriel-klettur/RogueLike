import os
import json
import pygame
import pytest
from roguelike_editors.items.controller.editor_controller import ItemEditorController
from roguelike_game.ecs.components.item_models import load_items

@pytest.fixture(autouse=True)
def init_pygame(monkeypatch):
    os.environ['SDL_VIDEODRIVER'] = 'dummy'
    pygame.display.init()
    pygame.display.set_mode((800,600))
    yield
    pygame.display.quit()

@pytest.fixture
def tmp_data_dir(tmp_path, monkeypatch):
    data_dir = tmp_path / "data"
    data_dir.mkdir()
    items_file = data_dir / "items.json"
    orig = {"item1": {"id": "item1", "name": "Test", "description": "Desc", "stackable": False}}
    items_file.write_text(json.dumps(orig), encoding='utf-8')
    monkeypatch.chdir(tmp_path)
    return items_file

@pytest.fixture
def controller(tmp_data_dir):
    items = load_items(str(tmp_data_dir))
    assets = {}
    font = pygame.font.SysFont(None, 12)
    ctrl = ItemEditorController(items, assets, font)
    ctrl.model.visible = True
    return ctrl

def test_single_click_property_sets_focus(controller):
    controller.model.property_entries = [(pygame.Rect(0,0,50,10),"prop1")]
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos":(10,5),"button":1,"clicks":1})
    controller.handle_event(event)
    assert controller.model.focused_property == "prop1"

def test_double_click_property_starts_edit(controller):
    controller.model.property_entries = [(pygame.Rect(0,0,50,10),"prop1")]
    controller.model.selected_item_id = "item1"
    setattr(controller.model.items["item1"], "prop1", 5)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos":(10,5),"button":1,"clicks":2})
    controller.handle_event(event)
    assert controller.model.editing_property == "prop1"
    assert controller.model.focused_property == "prop1"
    assert controller.model.editing_text == "5"

def test_commit_edit_int_conversion(tmp_data_dir, controller):
    # Prepare editing state
    controller.model.selected_item_id = "item1"
    controller.model.editing_property = "prop1"
    setattr(controller.model.items["item1"], "prop1", 0)
    controller.model.editing_text = "123"
    # Extend JSON file
    data = json.loads(tmp_data_dir.read_text(encoding='utf-8'))
    data["item1"]["prop1"] = 0
    tmp_data_dir.write_text(json.dumps(data), encoding='utf-8')
    # Commit
    controller._commit_edit()
    # Check model
    assert getattr(controller.model.items["item1"], "prop1") == 123
    # Check JSON file
    saved = json.loads(tmp_data_dir.read_text(encoding='utf-8'))
    assert saved["item1"]["prop1"] == 123

def test_click_outside_property_commits(controller):
    r = pygame.Rect(0,0,20,20)
    controller.model.property_entries = [(r, "prop1")]
    controller.model.editing_property = "prop1"
    controller.model.selected_item_id = "item1"
    setattr(controller.model.items["item1"], "prop1", 0)
    controller.model.editing_text = "42"
    # Click inside => no commit
    e_inside = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos":(10,10),"button":1})
    controller.handle_event(e_inside)
    assert controller.model.editing_property == "prop1"
    # Click outside => commit
    e_out = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos":(100,100),"button":1})
    controller.handle_event(e_out)
    assert controller.model.editing_property is None

def test_click_panel_preserves_focus(controller):
    controller.model.panel_rect = pygame.Rect(100,100,50,50)
    controller.model.focused_property = "prop1"
    e = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos":(110,110),"button":1})
    controller.handle_event(e)
    assert controller.model.focused_property == "prop1"
