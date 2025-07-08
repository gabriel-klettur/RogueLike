import os
import json
import pygame
import pytest
from roguelike_editors.items.view.editor_view import ItemEditorView
from roguelike_editors.items.model.editor_model import ItemEditorModel
from roguelike_game.ecs.components.item_models import load_items

@pytest.fixture(autouse=True)
def init_pygame():
    os.environ['SDL_VIDEODRIVER'] = 'dummy'
    pygame.display.init()
    pygame.font.init()
    pygame.display.set_mode((800,600))
    yield
    pygame.font.quit()
    pygame.display.quit()

@pytest.fixture
def tmp_data_and_model(tmp_path):
    # Create temp JSON
    data_dir = tmp_path / "data"
    data_dir.mkdir()
    items_file = data_dir / "items.json"
    orig = {"item1": {"id": "item1", "name": "Test", "description": "Desc", "stackable": False}}
    items_file.write_text(json.dumps(orig), encoding='utf-8')
    # Load items
    items = load_items(str(items_file))
    assets = {"item1": pygame.Surface((32,32))}
    font = pygame.font.SysFont(None, 12)
    model = ItemEditorModel(items=items, assets=assets)
    return model, assets, font


def test_draw_registers_property_entries_and_panel_rect(tmp_data_and_model):
    model, assets, font = tmp_data_and_model
    view = ItemEditorView(assets, font)
    # Set visible and select item
    model.visible = True
    model.selected_item_id = "item1"
    # Draw
    screen = pygame.Surface((800,600))
    view.draw(screen, model)
    # Should have property_entries
    assert hasattr(model, 'property_entries')
    assert isinstance(model.property_entries, list)
    assert len(model.property_entries) >= 1
    # Should have panel_rect
    assert hasattr(model, 'panel_rect')
    rect = model.panel_rect
    assert isinstance(rect, pygame.Rect)
    # panel_rect should be on right side
    assert rect.x > 0 and rect.y >= 0
    assert rect.width > 0 and rect.height > 0
