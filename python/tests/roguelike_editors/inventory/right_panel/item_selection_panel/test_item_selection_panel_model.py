import pytest
import pygame
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel


def test_initial_state():
    items = ['a', 'b']
    model = ItemSelectionPanelModel(items, visible_count=4)
    # Tabs
    assert model.default_items == items
    assert model.ground_items == []
    assert model.current_tab == 'default'
    assert model.available_items == items
    # List
    assert model.visible_count == 4
    assert model.scroll_offset == 0
    assert model.selected_item is None
    assert model.selected_index is None
    # Input
    assert model.quantity == 1
    # Title
    assert model.show_panel is False
    # Button (drag)
    assert isinstance(model.drag_offset, pygame.Vector2)
    assert model.drag_offset.x == 0 and model.drag_offset.y == 0
    assert model.dragging is False
    assert isinstance(model.drag_start_pos, pygame.Vector2)
    assert model.drag_start_pos.x == 0 and model.drag_start_pos.y == 0


def test_setters_affect_submodels():
    model = ItemSelectionPanelModel(['x'], visible_count=2)
    # Tabs properties
    model.default_items = ['d1']
    assert model.default_items == ['d1']
    model.ground_items = ['g1']
    assert model.ground_items == ['g1']
    model.available_items = ['d2']
    assert model.available_items == ['d2']
    model.current_tab = 'ground'
    assert model.current_tab == 'ground'
    # List properties
    model.visible_count = 5
    assert model.visible_count == 5
    model.scroll_offset = 3
    assert model.scroll_offset == 3
    model.selected_item = 'item'
    assert model.selected_item == 'item'
    model.selected_index = 7
    assert model.selected_index == 7
    # Input property
    model.quantity = 9
    assert model.quantity == 9
    # Title property
    model.show_panel = True
    assert model.show_panel is True
    # Button (drag)
    vec = pygame.Vector2(2, 3)
    model.drag_offset = vec
    assert model.drag_offset == vec
    model.dragging = True
    assert model.dragging is True
    pos = pygame.Vector2(4, 5)
    model.drag_start_pos = pos
    assert model.drag_start_pos == pos
