import types
import pygame

import roguelike_engine.map.view.chunked_map_view as cmv
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.model.layer import Layer


def make_map(w, h):
    matrix = ["." * w for _ in range(h)]
    layers = {Layer.Ground: [["" for _ in range(w)] for _ in range(h)]}
    return types.SimpleNamespace(matrix=matrix, layers=layers)


def test_chunked_map_view_large_positive_offsets_produce_empty_dirty(monkeypatch):
    # Avoid asset loads
    monkeypatch.setattr(cmv, 'get_sprite_for_tile', lambda ch, code: pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA), raising=True)

    view = cmv.ChunkedMapView(chunk_size=4)
    map_model = make_map(8, 8)  # 8x8 tiles

    # Screen smaller than map to ensure visibility calculations matter
    screen = pygame.Surface((4 * TILE_SIZE, 4 * TILE_SIZE), flags=pygame.SRCALPHA)

    # Offset beyond the map bounds -> no chunks should be visible
    camera = types.SimpleNamespace(zoom=1.0, offset_x=9999, offset_y=9999, apply=lambda pos: pos)

    dirty = view.render(screen, camera, map_model)
    assert dirty == []


def test_chunked_map_view_extreme_zoom_values_do_not_crash(monkeypatch):
    # Return a constant sprite
    monkeypatch.setattr(cmv, 'get_sprite_for_tile', lambda ch, code: pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA), raising=True)

    view = cmv.ChunkedMapView(chunk_size=2)
    map_model = make_map(4, 4)

    screen = pygame.Surface((64, 64), flags=pygame.SRCALPHA)

    # Extremely large zoom
    camera_high = types.SimpleNamespace(zoom=1000.0, offset_x=0, offset_y=0, apply=lambda pos: pos)
    dirty_high = view.render(screen, camera_high, map_model)
    assert isinstance(dirty_high, list)

    # Negative/zero zoom is clamped internally -> still returns list
    camera_low = types.SimpleNamespace(zoom=0.0, offset_x=0, offset_y=0, apply=lambda pos: pos)
    dirty_low = view.render(screen, camera_low, map_model)
    assert isinstance(dirty_low, list)
