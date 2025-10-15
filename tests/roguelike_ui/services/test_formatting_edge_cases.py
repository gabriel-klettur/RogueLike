from roguelike_ui.services.formatting import format_key_label


def test_edge_cases_and_mappings():
    # None -> em dash placeholder
    assert format_key_label(None) == "—"

    # Mouse extra buttons
    assert format_key_label("M_X1") == "Mouse Button 4"
    assert format_key_label("M_X2") == "Mouse Button 5"

    # Function keys via pygame constant name path
    assert format_key_label("K_F4") == "F4"

    # Keypad and modifiers normalization
    assert format_key_label("kp_1") == "Numpad 1"
    assert format_key_label("lctrl") == "Ctrl"
