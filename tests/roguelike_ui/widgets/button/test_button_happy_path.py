import pygame

from roguelike_ui.widgets.button import Button


def test_button_hover_and_draw():
    screen = pygame.Surface((50, 40), flags=pygame.SRCALPHA)
    btn = Button(rect=(5, 6, 20, 10))

    # Hover outside -> False
    assert btn.is_hovered((0, 0)) is False
    # Hover inside -> True
    assert btn.is_hovered((10, 8)) is True

    # Draw should not raise; with hover True renders overlay
    btn.draw(screen)

    # Render icon X should not raise
    btn.render_icon_x(screen)
