import pytest
import pygame

from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
from roguelike_game.ecs.components.physics.collider import Collider


def test_circle_collider_invalid_radius_raises():
    with pytest.raises(ValueError):
        CircleCollider(radius=0)
    with pytest.raises(ValueError):
        CircleCollider(radius=-5)


def test_collider_rect_type_and_dimensions():
    c = Collider(width=10, height=20, offset_x=1, offset_y=2)
    assert c.width == 10
    assert c.height == 20
    assert c.offset_x == 1
    assert c.offset_y == 2
    assert isinstance(c.rect, pygame.Rect)
    assert c.rect.size == (10, 20)
