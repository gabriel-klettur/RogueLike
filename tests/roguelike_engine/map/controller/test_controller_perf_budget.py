import time
import roguelike_engine.map.controller.map_service as ms


def test_build_map_perf_budget(monkeypatch):
    class Settings:
        zone_width = 8
        zone_height = 8
        global_width = 32
        global_height = 32
        dungeon_connect_side = "right"
        additional_zones = {}
        zone_offsets = {'lobby': (8, 8), 'dungeon': (16, 8)}
        @staticmethod
        def _dynamic_offsets():
            return {'lobby': (8, 8), 'dungeon': (16, 8)}

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

    start = time.perf_counter()
    ms.MapService().build_map()
    elapsed_ms = (time.perf_counter() - start) * 1000.0

    assert elapsed_ms < 100.0
