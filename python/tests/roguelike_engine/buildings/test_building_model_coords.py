import pygame
import pytest

from roguelike_engine.buildings.building_model import BuildingModel
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings


@pytest.fixture
def pygame_init():
    """Initialize and quit pygame around each test to ensure Surface can be created headlessly."""
    pygame.init()
    try:
        yield
    finally:
        pygame.quit()


@pytest.fixture
def patch_loader(monkeypatch):
    """Patch image loader to avoid filesystem I/O and return an in-memory Surface."""
    def fake_loader(_path: str) -> pygame.Surface:
        # Return a predictable size so grid math is stable
        return pygame.Surface((64, 64), pygame.SRCALPHA)

    # Patch the bound name used inside BuildingModel so its delegated call uses our fake
    monkeypatch.setattr("roguelike_engine.buildings.building_model.load_image", fake_loader)
    return fake_loader


def test_absolute_coords_no_zone_use_tile_size(pygame_init, patch_loader):
    # When zone is None or a sentinel ('no zone'), offsets are (0,0) in tiles.
    # x = 0*TILE_SIZE + rel_x; y = 0*TILE_SIZE + rel_y
    rel_x, rel_y = 10, 20
    m = BuildingModel(rel_x=rel_x, rel_y=rel_y, image_path="dummy.png", solid=True)
    m.zone = None

    assert m.x == rel_x
    assert m.y == rel_y


def test_absolute_coords_with_lobby_zone_uses_tile_size_multiplier(pygame_init, patch_loader):
    # For a real zone (e.g., 'lobby'), BuildingModel must convert tile offsets to pixels
    # using TILE_SIZE and add the per-building relative pixel offset.
    rel_x, rel_y = 7, 13
    m = BuildingModel(rel_x=rel_x, rel_y=rel_y, image_path="dummy.png", solid=True)
    m.zone = "lobby"

    ox, oy = global_map_settings.zone_offsets["lobby"]  # offsets are in tiles
    assert m.x == ox * TILE_SIZE + rel_x
    assert m.y == oy * TILE_SIZE + rel_y


def test_image_grid_size_respects_tile_size(pygame_init, patch_loader):
    # Ensure grid calculation maps image pixels to grid cells by TILE_SIZE.
    m = BuildingModel(rel_x=0, rel_y=0, image_path="dummy.png", solid=True)
    # Override image with known size to validate math precisely
    m.image = pygame.Surface((96, 64), pygame.SRCALPHA)  # width=96, height=64

    rows, cols = m._image_to_grid_size()
    assert rows == max(1, 64 // TILE_SIZE)
    assert cols == max(1, 96 // TILE_SIZE)


def test_setters_respect_tile_size_and_zone(pygame_init, patch_loader):
    # Given a lobby zone, setting absolute x/y must update rel_x/rel_y using TILE_SIZE*offset.
    base_rel_x, base_rel_y = 0, 0
    m = BuildingModel(rel_x=base_rel_x, rel_y=base_rel_y, image_path="dummy.png", solid=True)
    m.zone = "lobby"
    ox, oy = global_map_settings.zone_offsets["lobby"]

    new_rel_x, new_rel_y = 15, 27
    m.x = ox * TILE_SIZE + new_rel_x
    m.y = oy * TILE_SIZE + new_rel_y

    assert m.rel_x == new_rel_x
    assert m.rel_y == new_rel_y
