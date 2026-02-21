import types
from roguelike_engine.map.model.layer import Layer
import roguelike_engine.map.controller.map_service as ms


def test_build_map_tiles_layer_interop(monkeypatch):
    class Settings:
        zone_width = 5
        zone_height = 4
        global_width = 10
        global_height = 8
        dungeon_connect_side = "right"
        additional_zones = {}
        zone_offsets = {'lobby': (2, 2), 'dungeon': (7, 2)}
        @staticmethod
        def _dynamic_offsets():
            return {'lobby': (2, 2), 'dungeon': (7, 2)}

    monkeypatch.setattr(ms, 'global_map_settings', Settings, raising=True)

    class Gen:
        def generate(self, width, height, return_rooms=False):
            raw = [['#' for _ in range(width)] for _ in range(height)]
            return raw, {'rooms': [(1, 1, width - 2, height - 2)]}
    monkeypatch.setattr(ms, 'get_generator', lambda name: Gen(), raising=True)

    class Loader:
        def load(self, rows, key):
            # Build two layers: Ground and WallsTop
            h = len(rows)
            w = len(rows[0])
            ground_codes = [["g" for _ in range(w)] for _ in range(h)]
            walls_codes = [["w" for _ in range(w)] for _ in range(h)]
            tiles_by_layer = {Layer.Ground: [["TG" for _ in range(w)] for _ in range(h)],
                              Layer.WallsTop: [["TW" for _ in range(w)] for _ in range(h)]}
            layers = {Layer.Ground: ground_codes, Layer.WallsTop: walls_codes}
            return None, tiles_by_layer, layers
    monkeypatch.setattr(ms, 'get_map_loader', lambda name: Loader(), raising=True)

    monkeypatch.setattr(ms, 'generate_lobby_matrix', lambda: ['.' * Settings.zone_width for _ in range(Settings.zone_height)], raising=True)
    monkeypatch.setattr(ms, 'calculate_lobby_offset', lambda: Settings.zone_offsets['lobby'], raising=True)
    monkeypatch.setattr(ms, 'calculate_dungeon_offset', lambda off: Settings.zone_offsets['dungeon'], raising=True)

    # No-op tunnel
    monkeypatch.setattr(ms.DungeonGenerator, '_horiz_tunnel', staticmethod(lambda m, x1, x2, y: None), raising=True)
    monkeypatch.setattr(ms.DungeonGenerator, '_vert_tunnel', staticmethod(lambda m, y1, y2, x: None), raising=True)

    def fake_tiles_from_text(matrix, overlay):
        h = len(matrix)
        w = len(matrix[0]) if matrix else 0
        return [[types.SimpleNamespace(code=overlay[y][x] if overlay else '.') for x in range(w)] for y in range(h)]
    monkeypatch.setattr('roguelike_engine.map.model.map_model.load_tiles_from_text', fake_tiles_from_text, raising=True)

    m = ms.MapService().build_map()

    # Ground exists and overlay equals ground (legacy)
    assert Layer.Ground in m.layers
    assert m.overlay == m.layers[Layer.Ground]
    # Another layer must exist and be accessible
    assert Layer.WallsTop in m.tiles_by_layer
