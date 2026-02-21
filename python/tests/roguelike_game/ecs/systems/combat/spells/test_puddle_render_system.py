import types
import pygame
import pytest

from roguelike_game.ecs.systems.rendering.combat.spells.puddle_render_system import PuddleRenderSystem
from roguelike_game.ecs.components.abilities.puddle_component import PuddleComponent
from roguelike_game.ecs.components.transform.position import Position


def test_puddle_render_draws_translucent_circle():
    # Screen and camera
    pygame.display.init()
    screen = pygame.Surface((200, 200), pygame.SRCALPHA)

    class Cam:
        zoom = 1.0
        def apply(self, pos):
            return pos
    cam = Cam()

    # World with a puddle at center
    world = types.SimpleNamespace(components={
        'PuddleComponent': {},
        'Position': {},
    })
    eid = 1
    world.components['PuddleComponent'][eid] = PuddleComponent(
        radius=20, duration=5.0, tick_period=0.5, element='lava', color=(255, 120, 60), alpha=128
    )
    world.components['Position'][eid] = Position(100, 100)

    # Act
    PuddleRenderSystem().update(world, screen, cam)

    # Assert: center pixel should not be fully black due to blitted circle
    center_color = screen.get_at((100, 100))
    assert any(c > 0 for c in center_color[:3])
