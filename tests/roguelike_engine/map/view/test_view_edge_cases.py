import types
import pygame

import roguelike_engine.map.view.chunked_map_view as cmv
import roguelike_engine.map.view.map_view as mv
from roguelike_engine.map.model.layer import Layer


def make_sprite(size=(16, 16), color=(50, 150, 200, 255)):
    s = pygame.Surface(size, flags=pygame.SRCALPHA)
    s.fill(color)
    return s


def test_chunked_map_view_renders_with_min_zoom_and_mismatched_layers(monkeypatch):
    # Monkeypatch sprite loader to avoid assets
    monkeypatch.setattr(cmv, 'get_sprite_for_tile', lambda ch, code: make_sprite(), raising=True)

    view = cmv.ChunkedMapView(chunk_size=2)

    # Fake map model with matrix 3x3 and a layer grid with an extra row/col (mismatch)
    matrix = ["...", "...", "..."]
    ground_codes = [["", "", ""], ["", "", ""], ["", "", ""], ["", "", ""]]  # one extra row
    layers = {Layer.Ground: ground_codes}
    map_model = types.SimpleNamespace(matrix=matrix, layers=layers)

    # Camera with near-zero zoom (clamped internally) and zero offsets
    camera = types.SimpleNamespace(zoom=0.0, offset_x=0, offset_y=0, apply=lambda pos: pos)

    screen = pygame.Surface((64, 64), flags=pygame.SRCALPHA)

    # Render should build cache and return some dirty rects without crashing
    dirty = view.render(screen, camera, map_model)
    assert isinstance(dirty, list)

    # Subsequent update_chunks should rebuild only affected chunk
    # Affect tile at (row=1, col=1)
    view.update_chunks(map_model, camera, cells={(1, 1)})
    # Ensure cache exists for clamped zoom
    zoom_key = max(float(getattr(camera, 'zoom', 1.0)) or 1.0, 0.1)
    assert zoom_key in view.chunks_by_zoom


def test_map_view_returns_empty_dirty_when_not_in_view(monkeypatch):
    # Fake ZoneView to observe calls
    class DummyZoneView:
        def __init__(self):
            self.calls = []
        def render_zone(self, screen, camera, zone_name, tiles):
            self.calls.append((zone_name, len(tiles)))

    monkeypatch.setattr(mv, 'ZoneView', DummyZoneView, raising=True)

    view = mv.MapView()
    # Camera is_in_view always False
    camera = types.SimpleNamespace(
        is_in_view=lambda x, y, size: False,
        apply=lambda pos: pos,
    )
    Tile = types.SimpleNamespace
    tiles_by_zone = {'A': [Tile(x=0, y=0), Tile(x=32, y=0)]}
    map_manager = types.SimpleNamespace(tiles_by_zone=tiles_by_zone)

    screen = pygame.Surface((50, 50))
    dirty = view.render(screen, camera, map_manager)

    # No visible tiles -> no dirty rects; ZoneView NOT called (optimization: skip empty zones)
    assert dirty == []
    assert len(view.zone_view.calls) == 0
