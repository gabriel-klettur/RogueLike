import pygame

from roguelike_ui.ui_blocker import clear_blockers, register_blocker, is_blocked


def test_ui_blocker_register_and_clear():
    clear_blockers()
    r1 = pygame.Rect(0, 0, 10, 10)
    r2 = pygame.Rect(20, 20, 5, 5)

    register_blocker(r1)
    register_blocker(r2)

    assert is_blocked(1, 1) is True
    assert is_blocked(22, 22) is True
    assert is_blocked(15, 15) is False

    clear_blockers()
    assert is_blocked(1, 1) is False
