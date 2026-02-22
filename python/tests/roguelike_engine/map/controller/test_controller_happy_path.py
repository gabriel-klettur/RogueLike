import types
import pytest

import roguelike_engine.map.controller.map_controller as mc
import roguelike_engine.map.controller.map_service as ms


def test_build_map_happy_path(monkeypatch):
    # Fake global settings (small deterministic grid)
    class Settings:
        zone_width = 4
        zone_height = 3
        global_width = 8
        global_height = 6
        dungeon_connect_side = "right"
        additional_zones = {}
        zone_offsets = {
            'lobby': (2, 1),
            'dungeon': (6, 1),
        }
        @staticmethod
        def calculate_offset(parent_off, side):
            x, y = parent_off
            if side == 'right':
                return x + Settings.zone_width, y
            if side == 'left':
                return x - Settings.zone_width, y
            if side == 'top':
                return x, y - Settings.zone_height
            return x, y + Settings.zone_height
        @staticmethod
        def _dynamic_offsets():
            # Provide defaults in case controller resolves renamed keys
            return {
                'lobby': (2, 1),
                'dungeon': (6, 1),
            }

    monkeypatch.setattr(ms, 'global_map_settings', Settings, raising=True)

    # Deterministic generator
    class Gen:
        def generate(self, width, height, return_rooms=False):
            raw = [['.' for _ in range(width)] for _ in range(height)]
            meta = {'rooms': [(0, 0, width - 1, height - 1)]}
            return raw, meta

    monkeypatch.setattr(ms, 'get_generator', lambda name: Gen(), raising=True)

    # Deterministic loader capturing inputs
    captured = {}
    class Loader:
        def load(self, rows, key):
            captured['key'] = key
            captured['rows'] = rows
            # Minimal layers/tiles
            from roguelike_engine.map.model.layer import Layer
            h = len(rows)
            w = len(rows[0])
            layers = {Layer.Ground: [["g" for _ in range(w)] for _ in range(h)]}
            tiles_by_layer = {Layer.Ground: [["T" for _ in range(w)] for _ in range(h)]}
            return None, tiles_by_layer, layers

    monkeypatch.setattr(ms, 'get_map_loader', lambda name: Loader(), raising=True)

    # Simple utils
    monkeypatch.setattr(ms, 'generate_lobby_matrix', lambda: ['.' * Settings.zone_width for _ in range(Settings.zone_height)], raising=True)
    monkeypatch.setattr(ms, 'calculate_lobby_offset', lambda: Settings.zone_offsets['lobby'], raising=True)
    monkeypatch.setattr(ms, 'calculate_dungeon_offset', lambda off: Settings.zone_offsets['dungeon'], raising=True)

    # No-op tunnels
    monkeypatch.setattr(ms.DungeonGenerator, '_horiz_tunnel', staticmethod(lambda m, x1, x2, y: None), raising=True)
    monkeypatch.setattr(ms.DungeonGenerator, '_vert_tunnel', staticmethod(lambda m, y1, y2, x: None), raising=True)

    # Tiles construction in Map model
    def fake_tiles_from_text(matrix, overlay):
        h = len(matrix)
        w = len(matrix[0]) if matrix else 0
        return [[types.SimpleNamespace(code=overlay[y][x] if overlay else '.') for x in range(w)] for y in range(h)]
    monkeypatch.setattr('roguelike_engine.map.model.map_model.load_tiles_from_text', fake_tiles_from_text, raising=True)

    # Recreate the default service after monkeypatching factories
    mc._default_service = ms.MapService()

    result = mc.build_map()

    # Validate Map object
    from roguelike_engine.map.model.map_model import Map
    from roguelike_engine.map.model.layer import Layer
    assert isinstance(result, Map)
    assert result.name == 'global_map'
    assert Layer.Ground in result.layers
    assert result.overlay is result.layers[Layer.Ground]
    assert isinstance(result.tiles_by_layer, dict)
    assert captured['key'] == 'global_map'
    # Matrix dimensions as configured
    assert len(result.matrix) == Settings.global_height
    assert all(len(r) == Settings.global_width for r in result.matrix)
    # Metadata contains lobby offset
    assert 'lobby_offset' in result.metadata
