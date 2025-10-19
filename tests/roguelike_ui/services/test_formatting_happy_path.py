import pytest

from roguelike_ui.services.formatting import format_key_label


def test_format_key_label_arrows_and_specials():
    assert format_key_label("K_UP") == "↑"
    assert format_key_label("K_DOWN") == "↓"
    assert format_key_label("K_LEFT") == "←"
    assert format_key_label("K_RIGHT") == "→"
    assert format_key_label("K_RETURN") == "Enter"


def test_format_key_label_mouse_and_letters():
    assert format_key_label("M_LEFT") == "Left Click"
    assert format_key_label("M_RIGHT") == "Right Click"
    assert format_key_label("a") == "A"


def test_format_key_label_unknown_and_empty():
    # Unknown pygame key -> falls back to trimmed name, prettified
    assert format_key_label("K_UNKNOWN") == "Unknown"
    # Empty -> em dash placeholder
    assert format_key_label("") == "—"
