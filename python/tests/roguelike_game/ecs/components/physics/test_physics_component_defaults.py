import pygame

from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider


def test_collider_constructs_with_dimensions():
    c = Collider(width=16, height=8)
    assert c.width == 16 and c.height == 8
    assert c.rect.size == (16, 8)


def test_circle_collider_constructs_with_positive_radius():
    cc = CircleCollider(radius=3)
    assert cc.radius == 3


def test_mask_collider_constructs_with_mask():
    mask = pygame.mask.Mask((4, 4))
    mc = MaskCollider(mask=mask)
    assert mc.mask is mask


def test_multi_collider_constructs_with_dict():
    c1 = Collider(4, 4)
    c2 = Collider(2, 6)
    multi = MultiCollider({"a": c1, "b": c2})
    assert multi.colliders["a"] is c1
    assert multi.colliders["b"] is c2
