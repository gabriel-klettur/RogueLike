# Path: tests/systems/rendering/test_death_timer_debug_system.py
import pygame
import pytest
from roguelike_game.ecs.systems.rendering.death_timer_debug_system import DeathTimerDebugSystem

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()


def test_debug_system_initialization_and_cache():
    sys = DeathTimerDebugSystem(font_size=24, color=(1, 2, 3))
    # Font initialized
    assert hasattr(sys.font, 'render')
    # Cache has entries 0 to 60
    assert isinstance(sys.text_cache, dict)
    assert len(sys.text_cache) == 61
    for i in range(0, 61):
        surf = sys.text_cache[i]
        assert isinstance(surf, pygame.Surface)