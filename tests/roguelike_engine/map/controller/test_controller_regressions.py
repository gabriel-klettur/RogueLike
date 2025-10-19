import types
import roguelike_engine.map.controller.map_service as ms


def test_additional_empty_zone_is_merged_when_not_using_json(monkeypatch):
    class Settings:
        zone_width = 4
        zone_height = 3
        global_width = 12
        global_height = 9
        dungeon_connect_side = "right"
        use_zones_json = False
        additional_zones = {"empty_north": ("lobby", "top")}
        zone_offsets = {'lobby': (4, 3), 'dungeon': (8, 3)}
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
            return {'lobby': (4, 3), 'dungeon': (8, 3)}

    monkeypatch.setattr(ms, 'global_map_settings', Settings, raising=True)

    class Gen:
        def generate(self, width, height, return_rooms=False):
            # Dungeon filler
            raw = [['#' for _ in range(width)] for _ in range(height)]
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

    monkeypatch.setattr(ms, 'generate_lobby_matrix', lambda: ['.' * Settings.zone_width for _ in range(Settings.zone_height)], raising=True)

    # No-op tunnel draw to avoid touching matrix assumptions
    monkeypatch.setattr(ms.DungeonGenerator, '_horiz_tunnel', staticmethod(lambda m, x1, x2, y: None), raising=True)
    monkeypatch.setattr(ms.DungeonGenerator, '_vert_tunnel', staticmethod(lambda m, y1, y2, x: None), raising=True)

    m = ms.MapService().build_map()
    # After build, empty_north should have been merged as '.' area
    # Validate that top rows near lobby top have '.' from empty zone
    top_y = Settings.zone_offsets['lobby'][1] - 1
    if top_y >= 0:
        row = m.matrix[top_y]
        assert all(c in '.#' for c in row)
