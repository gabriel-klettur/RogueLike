import types
import pygame

import roguelike_engine.map.view.chunked_map_view as cmv
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.model.layer import Layer


def test_chunked_map_view_dirty_rects_within_chunk_budget(monkeypatch):
    # Use simple constant sprite to avoid assets
    monkeypatch.setattr(cmv, 'get_sprite_for_tile', lambda ch, code: pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA), raising=True)

    # Map 16x16 tiles, chunk_size=4 -> 4x4 chunks
    size_tiles = 16
    matrix = ["." * size_tiles for _ in range(size_tiles)]
    layers = {Layer.Ground: [["" for _ in range(size_tiles)] for _ in range(size_tiles)]}
    map_model = types.SimpleNamespace(matrix=matrix, layers=layers)

    view = cmv.ChunkedMapView(chunk_size=4)

    # Screen shows 8x8 tiles -> at most 2x2 chunks visible = 4 dirty rects
    tiles_visible = 8
    screen_px = tiles_visible * TILE_SIZE
    screen = pygame.Surface((screen_px, screen_px), flags=pygame.SRCALPHA)

    camera = types.SimpleNamespace(zoom=1.0, offset_x=0, offset_y=0, apply=lambda pos: pos)

    dirty = view.render(screen, camera, map_model)

    assert 0 < len(dirty) <= 4
