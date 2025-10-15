import pygame

from roguelike_ui.widgets.button import Button


def test_button_edge_cases_hover_and_rendering():
    screen = pygame.Surface((10, 10), flags=pygame.SRCALPHA)

    # Zero-size button should not crash drawing or hover checks
    btn_zero = Button(rect=(0, 0, 0, 0))
    assert btn_zero.is_hovered((0, 0)) is True  # Rect.collidepoint includes boundary
    btn_zero.draw(screen)
    btn_zero.render_icon_x(screen)

    # Non-overlapping hover remains False
    btn = Button(rect=(2, 2, 4, 4), border_width=0)
    assert btn.is_hovered((1, 1)) is False
    btn.draw(screen)

    # Custom hover color alpha and thickness in X icon
    btn.is_hovered((3, 3))
    btn.draw(screen)
    btn.render_icon_x(screen, color=(255, 0, 0), thickness=1, margin=1)
