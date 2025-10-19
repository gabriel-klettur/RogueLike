from roguelike_engine.buildings.services.collisions import image_to_grid_size, resample_collision_map
from roguelike_engine.buildings.services.zones import zone_offset
from roguelike_engine.config.config_tiles import TILE_SIZE
import pygame


def test_image_to_grid_size_happy_path(pygame_init):
    surf = pygame.Surface((65, 33), flags=pygame.SRCALPHA)
    rows, cols = image_to_grid_size(surf, TILE_SIZE)
    # Ceil-like division
    assert (rows, cols) == (
        max(1, (33 + TILE_SIZE - 1) // TILE_SIZE),
        max(1, (65 + TILE_SIZE - 1) // TILE_SIZE),
    )


def test_zone_offset_existing_zone(monkeypatch):
    offsets = {"lobby": (2, 3)}
    assert zone_offset("lobby", offsets) == (2, 3)


def test_resample_collision_map_basic_pooling():
    old = [["#", "."], [".", "."]]
    # Downsample to 1x1 should keep '#'
    new = resample_collision_map(old, 1, 1)
    assert new == [["#"]]
