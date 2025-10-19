import pickle
import types
import roguelike_engine.map.controller.map_service as ms


def test_build_map_serializable(monkeypatch):
    class Settings:
        zone_width = 4
        zone_height = 3
        global_width = 8
        global_height = 6
        dungeon_connect_side = "right"
        additional_zones = {}
        zone_offsets = {'lobby': (2, 1), 'dungeon': (6, 1)}
        @staticmethod
        def _dynamic_offsets():
            return {'lobby': (2, 1), 'dungeon': (6, 1)}

    monkeypatch.setattr(ms, 'global_map_settings', Settings, raising=True)

    class Gen:
        def generate(self, width, height, return_rooms=False):
            raw = [['.' for _ in range(width)] for _ in range(height)]
            return raw, {'rooms': [(0, 0, width - 1, height - 1)]}
    monkeypatch.setattr(ms, 'get_generator', lambda name: Gen(), raising=True)

    class Loader:
        def load(self, rows, key):
            from roguelike_engine.map.model.layer import Layer
            h = len(rows)
            w = len(rows[0])
            layers = {Layer.Ground: [["g" for _ in range(w)] for _ in range(h)]}
            tiles_by_layer = {Layer.Ground: [["T" for _ in range(w)] for _ in range(h)]}
            return None, tiles_by_layer, layers
    monkeypatch.setattr(ms, 'get_map_loader', lambda name: Loader(), raising=True)

    def fake_tiles_from_text(matrix, overlay):
        h = len(matrix)
        w = len(matrix[0]) if matrix else 0
        return [[types.SimpleNamespace(code=overlay[y][x] if overlay else '.') for x in range(w)] for y in range(h)]
    monkeypatch.setattr('roguelike_engine.map.model.map_model.load_tiles_from_text', fake_tiles_from_text, raising=True)

    m = ms.MapService().build_map()

    blob = pickle.dumps(m)
    m2 = pickle.loads(blob)

    assert m2.matrix == m.matrix
    assert m2.layers.keys() == m.layers.keys()
    assert m2.metadata == m.metadata
    assert m2.name == m.name
