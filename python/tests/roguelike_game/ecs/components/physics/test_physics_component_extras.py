import pygame

from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.physics.collider import Collider


def test_mask_collider_construction_with_pygame_mask():
    mask = pygame.mask.Mask((8, 8))
    mc = MaskCollider(mask=mask, offset_x=2, offset_y=3)
    assert mc.mask is mask
    assert mc.offset_x == 2
    assert mc.offset_y == 3


def test_multi_collider_holds_named_colliders():
    feet = Collider(width=4, height=2)
    body = Collider(width=8, height=12, offset_x=1, offset_y=2)
    multi = MultiCollider(colliders={"feet": feet, "body": body})
    assert set(multi.colliders.keys()) == {"feet", "body"}
    assert multi.colliders["feet"] is feet
    assert multi.colliders["body"] is body
