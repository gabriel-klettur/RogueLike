import logging
import pygame
import pytest

from roguelike_engine.buildings.services.collisions import image_to_grid_size, resample_collision_map
from roguelike_engine.buildings.services.zones import zone_offset
from roguelike_engine.config.config_tiles import TILE_SIZE


def test_image_to_grid_size_none_surface_returns_1x1():
    rows, cols = image_to_grid_size(None, TILE_SIZE)
    assert (rows, cols) == (1, 1)


def test_zone_offset_missing_zone_warns(caplog):
    caplog.set_level(logging.WARNING)
    offsets = {"lobby": (1, 2)}
    _ = zone_offset("unknown", offsets)
    assert any("not found" in rec.getMessage().lower() for rec in caplog.records)


def test_zone_offset_no_zone_sentinel_no_warning(caplog):
    caplog.set_level(logging.WARNING)
    offsets = {"lobby": (1, 2)}
    _ = zone_offset("no zone", offsets)
    assert not any("not found" in rec.getMessage().lower() for rec in caplog.records)


def test_resample_collision_map_handles_empty():
    new = resample_collision_map([], 3, 2)
    assert new == [[".", "."], [".", "."], [".", "."]]
