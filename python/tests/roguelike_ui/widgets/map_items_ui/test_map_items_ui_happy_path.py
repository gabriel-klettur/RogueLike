import json
import pygame

from roguelike_ui.widgets.map_items_ui import MapItemsUI


def test_map_items_ui_draw_and_select(tmp_path, monkeypatch):
    # Create sample instances file
    data = {
        "inst-1": {"item_id": "potion", "position": {"x": 3, "y": 4}},
        "inst-2": {"item_id": "scroll", "tile": {"x": 10, "y": 2}},
    }
    path = tmp_path / "items_instances.json"
    path.write_text(json.dumps(data), encoding="utf-8")

    font = pygame.font.SysFont(None, 14)
    ui = MapItemsUI(font, str(path))

    surface = pygame.Surface((240, 120), flags=pygame.SRCALPHA)
    rect = pygame.Rect(10, 10, 200, 80)

    # Initial draw loads file and renders list
    ui.draw(surface, rect)
    assert ui.list_ui.items and len(ui.list_ui.items) == 2

    # Click roughly on the second line to select 'inst-2'
    line_h = font.get_linesize()
    click_pos = (rect.x + 5, rect.y + line_h + line_h // 2)
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=click_pos)
    selected = ui.handle_event(ev)

    assert selected == "inst-2"
    assert ui.selected_instance == "inst-2"
