import pygame

from roguelike_editors.spawner.spawner_templates_panel.list_templates.list_templates_view import ListTemplatesView


def make_model(items, **overrides):
    base = {
        'visible': True,
        'title': 'Templates',
        'panel_width': 360,
        'header_height': 28,
        'row_height': 20,
        'visible_rows': 2,
        'scroll_offset': 0,
        'items': items,
    }
    base.update(overrides)
    return type('M', (), base)()


def test_templates_view_buttons_hidden_when_empty():
    screen = pygame.Surface((800, 600))
    view = ListTemplatesView()
    model = make_model([])

    rect = view.render(model, screen, anchor=(10, 10))

    assert rect is not None
    assert view.row_button_rects == []


def test_templates_view_buttons_for_visible_rows_and_scroll():
    screen = pygame.Surface((800, 600))
    view = ListTemplatesView()
    items = ["t0", "t1", "t2"]
    model = make_model(items, visible_rows=2, scroll_offset=0)

    rect = view.render(model, screen, anchor=(10, 10))
    assert rect is not None
    assert len(view.row_button_rects) == 2
    assert [d['gidx'] for d in view.row_button_rects] == [0, 1]

    # Scroll by one and re-render
    model.scroll_offset = 1
    rect = view.render(model, screen, anchor=(10, 10))
    assert rect is not None
    assert len(view.row_button_rects) == 2
    assert [d['gidx'] for d in view.row_button_rects] == [1, 2]
