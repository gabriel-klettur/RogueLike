import pytest
from roguelike_editors.tiles.tiles_editor_config import (
    OUTLINE_SEL, OUTLINE_HOVER, OUTLINE_CHOICE,
    THUMB, COLS, PAD,
    CLR_BORDER, CLR_HOVER, CLR_SELECTION,
    TOOLS, ICON_PATHS_TILE_TOOLBAR,
    BTN_W, BTN_H, BASE_TILE_DIR,
    ARROW_UP_ICON, FOLDER_ICON,
    FILE_PATTERNS
)

def test_outline_colors():
    assert isinstance(OUTLINE_SEL, tuple) and len(OUTLINE_SEL) == 3
    assert isinstance(OUTLINE_HOVER, tuple) and len(OUTLINE_HOVER) == 3
    assert isinstance(OUTLINE_CHOICE, tuple) and len(OUTLINE_CHOICE) == 3


def test_dimensions_and_paths():
    assert isinstance(THUMB, int)
    assert isinstance(COLS, int)
    assert isinstance(PAD, int)
    assert isinstance(BTN_W, int)
    assert isinstance(BTN_H, int)
    assert isinstance(BASE_TILE_DIR, str)
    assert isinstance(ARROW_UP_ICON, str)
    assert isinstance(FOLDER_ICON, str)
    assert isinstance(FILE_PATTERNS, list)
    assert all(isinstance(p, str) for p in FILE_PATTERNS)


def test_tools_and_icons():
    # Tools list contains expected editor tools
    for tool in ["select", "brush", "eyedropper", "view", "delete"]:
        assert tool in TOOLS
    # icon paths keys match tools
    for key in ICON_PATHS_TILE_TOOLBAR:
        assert key in TOOLS
        assert isinstance(ICON_PATHS_TILE_TOOLBAR[key], str)
