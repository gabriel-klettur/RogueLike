from dataclasses import dataclass


@dataclass(frozen=True)
class Palette:
    """Centralized color palette for Map Editor UI."""
    overlay_bg: tuple[int, int, int, int]
    progress_bg: tuple[int, int, int]
    progress_fill: tuple[int, int, int]
    border_default: tuple[int, int, int]
    border_selected: tuple[int, int, int]
    border_hidden: tuple[int, int, int]
    border_delete: tuple[int, int, int]
    collider_fill: tuple[int, int, int, int]
    collider_border: tuple[int, int, int]
    input_bg: tuple[int, int, int]
    input_border: tuple[int, int, int]
    button_bg: tuple[int, int, int]
    button_border: tuple[int, int, int]
    button_text: tuple[int, int, int]
    text: tuple[int, int, int]
    dialog_bg: tuple[int, int, int]
    dialog_border: tuple[int, int, int]
    yes_bg: tuple[int, int, int]
    no_bg: tuple[int, int, int]


DEFAULT_PALETTE = Palette(
    overlay_bg=(0, 0, 0, 150),
    progress_bg=(50, 50, 50),
    progress_fill=(0, 150, 215),
    border_default=(0, 128, 255),
    border_selected=(0, 255, 0),
    border_hidden=(100, 100, 100),
    border_delete=(255, 0, 0),
    collider_fill=(255, 0, 0, 80),
    collider_border=(255, 0, 0),
    input_bg=(255, 255, 255),
    input_border=(0, 0, 0),
    button_bg=(0, 120, 215),
    button_border=(255, 255, 255),
    button_text=(255, 255, 255),
    text=(255, 255, 255),
    dialog_bg=(0, 0, 0),
    dialog_border=(255, 255, 255),
    yes_bg=(0, 200, 0),
    no_bg=(200, 0, 0),
)


def default_palette() -> Palette:
    """Factory for a modifiable copy of the default palette."""
    return DEFAULT_PALETTE
