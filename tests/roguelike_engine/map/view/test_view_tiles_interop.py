import types
import pygame

import roguelike_engine.map.view.chunked_map_view as cmv
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.model.layer import Layer


def test_chunked_map_view_draws_non_ground_overlays(monkeypatch):
    # Return a transparent sprite for ground blanks and colored for decorations
    def fake_sprite(ch, code):
        surf = pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA)
        if code == 'D':
            surf.fill((255, 0, 0, 255))
        return surf
    monkeypatch.setattr(cmv, 'get_sprite_for_tile', fake_sprite, raising=True)

    view = cmv.ChunkedMapView(chunk_size=1)

    # 1x1 map: ground blank, decorations 'D' should draw a red pixel
    matrix = ["."]
    layers = {
        Layer.Ground: [[""]],
        Layer.Decorations: [["D"]],
    }
    map_model = types.SimpleNamespace(matrix=matrix, layers=layers)

    camera = types.SimpleNamespace(zoom=1.0, offset_x=0, offset_y=0, apply=lambda pos: pos)
    screen = pygame.Surface((TILE_SIZE, TILE_SIZE), flags=pygame.SRCALPHA)

    dirty = view.render(screen, camera, map_model)
    assert dirty and len(dirty) >= 1

    # The top-left pixel should be red (decoration drawn)
    px = screen.get_at((0, 0))
    assert px.r == 255 and px.a == 255
