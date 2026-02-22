import types
import roguelike_engine.map.controller.map_service as ms


def test_controller_handles_renamed_zone_keys(monkeypatch):
    # Offsets do not contain 'lobby'/'dungeon' keys; they were renamed externally.
    class Settings:
        zone_width = 4
        zone_height = 3
        global_width = 8
        global_height = 6
        dungeon_connect_side = "right"
        additional_zones = {}
        zone_offsets = {
            'HUB': (2, 1),
            'CAVES': (6, 1),
        }
        @staticmethod
        def _dynamic_offsets():
            # dynamic offsets provide canonical values used by fallback resolution
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

    # Build should not raise and should use 'global_map' as default name
    m = ms.MapService().build_map()
    assert m.name == 'global_map'
