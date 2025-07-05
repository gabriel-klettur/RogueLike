import os
import pygame
import pytest
from roguelike_editors.items.controller.editor_controller import ItemEditorController

class DummyItem:
    def __init__(self, id):
        self.id = id
        self.name = 'Test'
        self.description = 'Desc'
        self.stackable = False

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
def controller():
    # Create 13 items to span two rows in a 12-column grid
    raw_items = {f'item{i}': DummyItem(f'item{i}') for i in range(13)}
    # Include placeholder to be excluded
    raw_items['image_item_not_found'] = DummyItem('image_item_not_found')
    assets = {k: pygame.Surface((32,32)) for k in raw_items.keys()}
    font = pygame.font.SysFont(None, 12)
    controller = ItemEditorController(raw_items, assets, font)
    controller.model.visible = True
    return controller

def test_hover_second_row(controller):
    # Center of cell at row 1, col 0
    font_h = controller.view.font.get_height()
    cell_height = 64 + 4 + font_h
    y = 20 + (cell_height + 20) * 1 + 32
    x = 20 + (64 + 20) * 0 + 32
    event = pygame.event.Event(pygame.MOUSEMOTION, pos=(x, y))
    controller.handle_event(event)
    assert controller.model.hovered_item_id == 'item12'
    assert controller.model.selected_item_id is None

def test_click_second_row(controller):
    # Center of cell at row 1, col 0
    font_h = controller.view.font.get_height()
    cell_height = 64 + 4 + font_h
    y = 20 + (cell_height + 20) * 1 + 32
    x = 20 + (64 + 20) * 0 + 32
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, pos=(x, y), button=1)
    controller.handle_event(event)
    assert controller.model.selected_item_id == 'item12'

def test_click_blank_cell(controller):
    # Center of cell at row 1, col 1 (blank)
    font_h = controller.view.font.get_height()
    cell_height = 64 + 4 + font_h
    y = 20 + (cell_height + 20) * 1 + 32
    x = 20 + (64 + 20) * 1 + 32
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, pos=(x, y), button=1)
    controller.handle_event(event)
    assert controller.model.selected_item_id is None
