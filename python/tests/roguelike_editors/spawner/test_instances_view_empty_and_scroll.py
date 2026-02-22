import pygame

from roguelike_editors.spawner.spawner_instances_panel.spawner_list_instances_view import SpawnerListInstancesView


def make_model(items, **overrides):
    base = {
        'visible': True,
        'title': 'Instances',
        'panel_width': 360,
        'header_height': 28,
        'row_height': 20,
        'visible_rows': 3,
        'scroll_offset': 0,
        'items': items,
        'empty_text': 'No instances',
        'empty_hint': 'Use Templates panel to add one',
    }
    base.update(overrides)
    return type('M', (), base)()


def test_instances_view_renders_when_empty():
    screen = pygame.Surface((800, 600))
    view = SpawnerListInstancesView()
    model = make_model([])

    rect = view.render(model, screen, anchor=(20, 20))

    assert rect is not None
    assert rect.left == 20 and rect.top == 20


def test_instances_view_scroll_does_not_crash_and_updates_rect():
    screen = pygame.Surface((800, 600))
    view = SpawnerListInstancesView()
    items = [f"inst_{i}" for i in range(10)]
    model = make_model(items, visible_rows=3, scroll_offset=0)

    rect1 = view.render(model, screen, anchor=(30, 30))
    assert rect1 is not None

    # Scroll and re-render
    model.scroll_offset = 5
    rect2 = view.render(model, screen, anchor=(30, 30))
    assert rect2 is not None
    # Same anchor -> same rect
    assert (rect1.left, rect1.top, rect1.width, rect1.height) == (
        rect2.left, rect2.top, rect2.width, rect2.height
    )
