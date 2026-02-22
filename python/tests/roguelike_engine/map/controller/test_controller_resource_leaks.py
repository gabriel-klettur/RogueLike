import copy
import types
import roguelike_engine.map.controller.map_service as ms


def test_build_map_does_not_mutate_zone_offsets(monkeypatch):
    # Prepare a baseline offsets mapping and ensure it remains unchanged
    base_offsets = {
        'lobby': (2, 1),
        'dungeon': (6, 1),
    }

    class Settings:
        zone_width = 4
        zone_height = 3
        global_width = 8
        global_height = 6
        dungeon_connect_side = "right"
        additional_zones = {}
        zone_offsets = copy.deepcopy(base_offsets)
        use_zones_json = True
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

    before = copy.deepcopy(Settings.zone_offsets)
    ms.MapService().build_map()
    after = Settings.zone_offsets

    # With use_zones_json=True and no additional_zones, controller should not mutate offsets
    assert after == before == base_offsets
