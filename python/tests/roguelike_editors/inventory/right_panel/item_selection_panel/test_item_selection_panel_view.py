import pytest
import pygame
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_view import ItemSelectionPanelView
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_view():
    font = pygame.font.SysFont(None, 24)
    view = ItemSelectionPanelView(font, margin=2, button_size=(80, 20))
    # stub nested views
    view.tittle_view.draw = lambda surface, panel_rect: {'t': True}
    view.tabs_view.draw = lambda surface, current_tab, default_rect, ground_rect: {'ta': True}
    view.list_view.draw = lambda surface, items, scroll_rect, line_h, current_tab, selected_item, selected_index: {'l': True}
    view.input_view.draw = lambda surface, quantity, input_rect: {'i': True}
    view.button_view.draw = lambda surface, add_btn_rect: {'b': True}
    return view


def test_draw_returns_empty_dict_when_panel_hidden(setup_view):
    view = setup_view
    model = ItemSelectionPanelModel(['x'], visible_count=1)
    base_rect = pygame.Rect(0, 0, 50, 50)
    model.show_panel = False
    result = view.draw(pygame.Surface((100, 100)), model, base_rect)
    assert result == {}


def test_draw_merges_subview_dicts(setup_view):
    view = setup_view
    model = ItemSelectionPanelModel(['x', 'y'], visible_count=2)
    model.show_panel = True
    base_rect = pygame.Rect(0, 0, 100, 100)
    surface = pygame.Surface((200, 200))
    result = view.draw(surface, model, base_rect)
    assert result == {'t': True, 'ta': True, 'l': True, 'i': True, 'b': True}


def test_draw_positions_panel_based_on_drag_offset(setup_view):
    view = setup_view
    model = ItemSelectionPanelModel(['a'], visible_count=1)
    model.show_panel = True
    # apply drag offset
    model.drag_offset = pygame.Vector2(10, 15)
    base_rect = pygame.Rect(0, 0, 50, 50)
    surface = pygame.Surface((200, 200))
    # call draw to compute panel_rect (but stub returns only subviews)
    view.draw(surface, model, base_rect)
    # ensure drag_offset was used in position calculation by verifying x and y shift
    # panel_rect is passed to tittle_view.draw as panel_rect. To capture it, override draw to record panel_rect
    recorded = {}
    def tittle_draw_rec(surface_arg, panel_rect_arg):
        recorded['rect'] = panel_rect_arg
        return {}
    view.tittle_view.draw = tittle_draw_rec
    # other stubs
    view.tabs_view.draw = lambda *args, **kwargs: {}
    view.list_view.draw = lambda *args, **kwargs: {}
    view.input_view.draw = lambda *args, **kwargs: {}
    view.button_view.draw = lambda *args, **kwargs: {}
    view.draw(surface, model, base_rect)
    rect = recorded['rect']
    w = base_rect.width
    line_h = view.font.get_linesize()
    visible = min(len(model.default_items), model.visible_count)
    scroll_h = visible * line_h + 2 * view.margin
    input_h = line_h + 2 * view.margin
    button_h = view.button_size[1]
    tab_h = line_h + view.margin
    panel_h = tab_h + scroll_h + view.margin + input_h + view.margin + button_h + view.margin
    sw, sh = surface.get_size()
    expected_x = sw - w - view.margin + int(model.drag_offset.x)
    expected_y = sh - panel_h - view.margin + int(model.drag_offset.y)
    assert rect.x == expected_x and rect.y == expected_y
