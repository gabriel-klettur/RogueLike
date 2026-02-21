import pytest
import pygame
from roguelike_editors.inventory.right_panel.item_selection_panel.button.button_model import ButtonModel

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

def test_default_values():
    model = ButtonModel()
    assert model.drag_offset == pygame.Vector2(0, 0)
    assert model.dragging is False
    assert model.drag_start_pos == pygame.Vector2(0, 0)

def test_property_assignment():
    model = ButtonModel()
    offset = pygame.Vector2(5, 10)
    model.drag_offset = offset
    model.dragging = True
    start = pygame.Vector2(2, 3)
    model.drag_start_pos = start
    assert model.drag_offset == offset
    assert model.dragging is True
    assert model.drag_start_pos == start
