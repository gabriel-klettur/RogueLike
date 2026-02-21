import types
import roguelike_engine.map.controller.map_controller as mc
import roguelike_engine.map.controller.map_service as ms


def test_build_map_no_rooms(monkeypatch):
    class Settings:
        zone_width = 6
        zone_height = 4
        global_width = 12
        global_height = 8
        dungeon_connect_side = "right"
        additional_zones = {}
        use_zones_json = False
        zone_offsets = {
            'lobby': (3, 2),
            'dungeon': (9, 2),
        }
        @staticmethod
        def calculate_offset(parent_off, side):
            x, y = parent_off
            return {
                'right': (x + Settings.zone_width, y),
                'left': (x - Settings.zone_width, y),
                'top': (x, y - Settings.zone_height),
                'bottom': (x, y + Settings.zone_height),
            }[side]
        @staticmethod
        def _dynamic_offsets():
            return {'lobby': (3, 2), 'dungeon': (9, 2)}

    monkeypatch.setattr(ms, 'global_map_settings', Settings, raising=True)

    class Gen:
        def generate(self, width, height, return_rooms=False):
            raw = [['.' for _ in range(width)] for _ in range(height)]
            meta = {'rooms': []}  # edge case: no rooms
            return raw, meta
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

    monkeypatch.setattr(ms, 'generate_lobby_matrix', lambda: ['.' * Settings.zone_width for _ in range(Settings.zone_height)], raising=True)
    monkeypatch.setattr(ms, 'calculate_lobby_offset', lambda: Settings.zone_offsets['lobby'], raising=True)
    monkeypatch.setattr(ms, 'calculate_dungeon_offset', lambda off: Settings.zone_offsets['dungeon'], raising=True)

    monkeypatch.setattr(ms.DungeonGenerator, '_horiz_tunnel', staticmethod(lambda m, x1, x2, y: None), raising=True)
    monkeypatch.setattr(ms.DungeonGenerator, '_vert_tunnel', staticmethod(lambda m, y1, y2, x: None), raising=True)

    def fake_tiles_from_text(matrix, overlay):
        h = len(matrix)
        w = len(matrix[0]) if matrix else 0
        return [[types.SimpleNamespace(code=overlay[y][x] if overlay else '.') for x in range(w)] for y in range(h)]
    monkeypatch.setattr('roguelike_engine.map.model.map_model.load_tiles_from_text', fake_tiles_from_text, raising=True)

    mc._default_service = ms.MapService()
    m = mc.build_map()

    from roguelike_engine.map.model.layer import Layer
    assert m.layers[Layer.Ground]
    assert isinstance(m.metadata, dict)
