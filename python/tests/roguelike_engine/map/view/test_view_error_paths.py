import types
import pygame

import roguelike_engine.map.view.chunked_map_view as cmv
from roguelike_engine.map.model.layer import Layer


def test_chunked_map_view_handles_missing_sprites(monkeypatch):
    # Force sprite loader to return None for all tiles
    monkeypatch.setattr(cmv, 'get_sprite_for_tile', lambda ch, code: None, raising=True)

    view = cmv.ChunkedMapView(chunk_size=2)

    # 3x3 map, codes all blank (Ground draws even if blank)
    matrix = ["...", "...", "..."]
    ground_codes = [["", "", ""], ["", "", ""], ["", "", ""]]
    layers = {Layer.Ground: ground_codes}
    map_model = types.SimpleNamespace(matrix=matrix, layers=layers)

    camera = types.SimpleNamespace(zoom=1.0, offset_x=0, offset_y=0, apply=lambda pos: pos)
    screen = pygame.Surface((64, 64), flags=pygame.SRCALPHA)

    # Should not crash even if sprites are missing (no blits)
    dirty = view.render(screen, camera, map_model)
    assert isinstance(dirty, list)

    # Updating chunks with a cell inside range should also not crash
    view.update_chunks(map_model, camera, cells={(1, 1)})
