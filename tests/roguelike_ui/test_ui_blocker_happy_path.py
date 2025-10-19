import pygame

from roguelike_ui.ui_blocker import clear_blockers, register_blocker, is_blocked


def test_ui_blocker_happy_path_basic_blocking():
    clear_blockers()
    # Start with no blockers
    assert is_blocked(0, 0) is False

    # Register one panel
    rect = pygame.Rect(10, 10, 20, 20)
    register_blocker(rect)

    # Inside is blocked, outside is not
    assert is_blocked(15, 20) is True
    assert is_blocked(50, 50) is False

    # Add another blocker; both should be considered
    register_blocker(pygame.Rect(0, 0, 5, 5))
    assert is_blocked(1, 1) is True
