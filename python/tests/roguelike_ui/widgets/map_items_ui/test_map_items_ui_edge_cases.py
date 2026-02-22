import json
import pygame

from roguelike_ui.widgets.map_items_ui import MapItemsUI


def test_map_items_ui_missing_file_and_tile_fallback(tmp_path):
    # Non-existing file path
    missing_path = tmp_path / "no_file.json"

    font = pygame.font.SysFont(None, 14)
    ui = MapItemsUI(font, str(missing_path))

    surface = pygame.Surface((200, 100), flags=pygame.SRCALPHA)
    rect = pygame.Rect(5, 5, 180, 80)

    # Draw should not crash and list should be empty
    ui.draw(surface, rect)
    assert ui.list_ui.items == []

    # Click anywhere returns None
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(rect.x + 10, rect.y + 10))
    assert ui.handle_event(ev) is None

    # Now create a file with only 'tile' coords to exercise fallback formatting
    data = {"i1": {"item_id": "scroll", "tile": {"x": 7, "y": 9}}}
    path2 = tmp_path / "instances.json"
    path2.write_text(json.dumps(data), encoding="utf-8")

    ui2 = MapItemsUI(font, str(path2))
    ui2.draw(surface, rect)
    assert ui2.list_ui.items and "i1:" in ui2.list_ui.items[0]
    # Should contain the coordinates string
    assert "@(" in ui2.list_ui.items[0] and ")" in ui2.list_ui.items[0]
