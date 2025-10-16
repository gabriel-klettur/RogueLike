import pygame
import pytest

from roguelike_engine.buildings.building_model import BuildingModel
from roguelike_engine.config.config_tiles import TILE_SIZE


def test_collision_rect_and_tiles_happy_path(pygame_init, patch_loader):
    patch_loader(size=(64, 64))
    m = BuildingModel(rel_x=10, rel_y=20, image_path="dummy.png", solid=True, split_ratio=0.5)
    # 2x2 grid; only bottom-right solid
    m.collision_map = [[".", "."], [".", "#"]]

    # collision_rect corresponds to bottom part (split at 32px)
    rect = m.collision_rect
    assert rect.topleft == (m.x, m.y + 32)
    assert rect.size == (64, 32)

    tiles = m.collision_tiles
    assert len(tiles) == 1
    t = tiles[0]
    # Row=1, Col=1
    assert t.topleft == (m.x + TILE_SIZE, m.y + TILE_SIZE)
    assert t.size == (TILE_SIZE, TILE_SIZE)

    # tile objs mirror tiles
    objs = m.collision_tile_objs
    assert len(objs) == 1 and objs[0].solid is True and isinstance(objs[0].rect, pygame.Rect)
