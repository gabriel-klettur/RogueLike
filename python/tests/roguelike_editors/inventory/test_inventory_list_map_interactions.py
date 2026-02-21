import pygame
import pytest
from types import SimpleNamespace

from roguelike_editors.inventory.left_panel.list.list_view import ListView
from roguelike_editors.inventory.left_panel.list.list_event_handler import ListEventHandler


@pytest.fixture()
def font():
    return pygame.font.Font(None, 18)


def make_surface(w=400, h=200):
    surf = pygame.Surface((w, h))
    surf.fill((0, 0, 0))
    return surf


def test_map_hover_draws_yellow_line_and_orange_only_on_coords(monkeypatch, font):
    # Arrange
    view = ListView(font, margin=7)
    base_rect = pygame.Rect(10, 10, 320, 120)
    surface = make_surface(base_rect.width, base_rect.height)

    # Map items example (format similar to ListController._get_other_items)
    items = ["torch x1 @(10.0,20.0)"]
    model = SimpleNamespace(current_category='map')

    # Mouse positioned over the first line inside the panel
    line_h = font.get_linesize()
    mouse_pos = (base_rect.x + 20, base_rect.y + line_h // 2)
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: mouse_pos)

    rect_calls = []

    def fake_draw_rect(surface_arg, color, rect, width=0):
        # copy rect to detach from internal mutations
        rect_calls.append((tuple(color), pygame.Rect(rect), int(width)))

    monkeypatch.setattr(pygame.draw, 'rect', fake_draw_rect)

    # Act
    view.draw(surface, model, base_rect, items)

    # Assert: expect one yellow full-line hover and one orange coords-only rect
    yellow = [c for c in rect_calls if c[0] == (255, 255, 0)]
    orange = [c for c in rect_calls if c[0] == (255, 165, 0)]

    assert any(r.height == line_h and r.x == base_rect.x for (_, r, w) in yellow), \
        "Debe dibujar el borde amarillo sobre la línea completa"

    # Compute expected orange rect bounds using the same font metrics
    text = items[0]
    start = text.find('@(')
    end = text.find(')', start)
    assert start != -1 and end != -1
    prefix = text[:start]
    coords = text[start:end + 1]

    text_x = base_rect.x + view.scroll_panel.margin
    prefix_w = font.size(prefix)[0]
    coords_w = font.size(coords)[0]

    assert any(
        (r.y == base_rect.y and r.height == line_h and r.x == text_x + prefix_w and r.width == coords_w)
        for (_, r, w) in orange
    ), "El borde naranja debe cubrir solo el substring de coordenadas @(x,y)"


def test_map_click_press_and_hold_centers_camera_and_restores_on_release(font):
    # Arrange controller/view/model stubs
    class Camera:
        def __init__(self):
            self.last_target = None
        def update(self, target):
            self.last_target = SimpleNamespace(x=getattr(target, 'x'), y=getattr(target, 'y'))

    player_pos = SimpleNamespace(x=5.0, y=6.0)
    world = SimpleNamespace(
        player_entity=42,
        components={'Position': {42: player_pos}}
    )
    game = SimpleNamespace(camera=Camera())
    editor_model = SimpleNamespace(overlay_hidden_while_hold=False, holding_pos_focus=False)
    editor_controller = SimpleNamespace(game=game, world=world, model=editor_model)

    items = ["potion x2 @(10.0,20.0)"]

    class StubController:
        def get_items_list(self):
            return items
        def select_entity(self, eid):
            # not used in map branch but present for interface parity
            pass

    class StubView:
        def __init__(self, font_obj):
            self.font = font_obj
            self.panel_rect = pygame.Rect(10, 10, 320, 100)
            self.list_view = SimpleNamespace(scroll_panel=SimpleNamespace(scroll_offset=0))

    model = SimpleNamespace(current_category='map')

    handler = ListEventHandler(editor_controller, StubController(), StubView(font), model)

    # Click position: on the first line
    line_h = font.get_linesize()
    click_pos = (15, 10 + line_h // 2)

    # Act: press
    evt_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': click_pos})
    handled_down = handler.handle(evt_down)

    # Assert after press: camera focused on coords and overlay flags set
    assert handled_down is True
    assert editor_model.overlay_hidden_while_hold is True
    assert editor_model.holding_pos_focus is True
    assert game.camera.last_target == SimpleNamespace(x=10.0, y=20.0)

    # Act: release
    evt_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': click_pos})
    handled_up = handler.handle(evt_up)

    # Assert after release: overlay restored and camera back to player
    assert handled_up is True
    assert editor_model.overlay_hidden_while_hold is False
    assert editor_model.holding_pos_focus is False
    assert game.camera.last_target == SimpleNamespace(x=player_pos.x, y=player_pos.y)
