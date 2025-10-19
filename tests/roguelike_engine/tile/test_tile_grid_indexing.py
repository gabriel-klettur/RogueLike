from types import SimpleNamespace

import pygame

from roguelike_engine.tile.utils.loader import load_tiles_from_text
from roguelike_engine.config.config_tiles import TILE_SIZE


def test_load_tiles_from_text_positions_and_rects(monkeypatch):
    # Avoid touching disk: return tiny surfaces for any sprite request
    def _fake_sprite(char, overlay):
        return pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA)
    monkeypatch.setattr("roguelike_engine.tile.utils.assets.get_sprite_for_tile", _fake_sprite)
    # 3x2 map
    map_rows = [
        ".#.",
        "D=O",
    ]
    tiles = load_tiles_from_text(map_rows)
    assert len(tiles) == 2 and len(tiles[0]) == 3

    for y, row in enumerate(tiles):
        for x, t in enumerate(row):
            assert t.x == x * TILE_SIZE
            assert t.y == y * TILE_SIZE
            assert t.rect.topleft == (t.x, t.y)
            assert t.rect.size == (TILE_SIZE, TILE_SIZE)
