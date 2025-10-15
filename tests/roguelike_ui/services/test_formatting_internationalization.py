from roguelike_ui.services.formatting import format_key_label


def test_international_letters_uppercase_and_preserved():
    # Latin with diacritics
    assert format_key_label("á") == "Á"
    assert format_key_label("ñ") == "Ñ"

    # Cyrillic example: lower to upper
    assert format_key_label("ж") == "Ж"


def test_mixed_case_and_whitespace_normalization():
    # Title-case fallback and trimming spaces for unknown labels
    assert format_key_label("  pageup  ") == "Page Up"
    assert format_key_label(" printscreen") == "Print Screen"
