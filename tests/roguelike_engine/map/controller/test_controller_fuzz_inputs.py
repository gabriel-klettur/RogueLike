import types
import random
import roguelike_engine.map.controller.map_service as ms


def test_build_map_fuzz_inputs_no_crash(monkeypatch):
    rng = random.Random(12345)

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

    # No-op tunnels and deterministic branch
    monkeypatch.setattr(ms.DungeonGenerator, '_horiz_tunnel', staticmethod(lambda m, x1, x2, y: None), raising=True)
    monkeypatch.setattr(ms.DungeonGenerator, '_vert_tunnel', staticmethod(lambda m, y1, y2, x: None), raising=True)
    monkeypatch.setattr(ms.random, 'random', lambda: 0.25, raising=True)

    for _ in range(20):
        zw = rng.randint(3, 12)
        zh = rng.randint(3, 12)
        gw = rng.randint(zw * 2, zw * 4)
        gh = rng.randint(zh * 2, zh * 4)
        lob_x = rng.randint(0, max(0, gw - zw))
        lob_y = rng.randint(0, max(0, gh - zh))
        side = rng.choice(["right", "left", "top", "bottom"])
        if side == "right":
            dun_x, dun_y = min(gw - zw, lob_x + zw), lob_y
        elif side == "left":
            dun_x, dun_y = max(0, lob_x - zw), lob_y
        elif side == "top":
            dun_x, dun_y = lob_x, max(0, lob_y - zh)
        else:
            dun_x, dun_y = lob_x, min(gh - zh, lob_y + zh)

        class Settings:
            zone_width = zw
            zone_height = zh
            global_width = gw
            global_height = gh
            dungeon_connect_side = side
            additional_zones = {}
            zone_offsets = {'lobby': (lob_x, lob_y), 'dungeon': (dun_x, dun_y)}
            use_zones_json = rng.choice([True, False])
            @staticmethod
            def calculate_offset(parent_off, s):
                x, y = parent_off
                return {
                    'right': (min(gw - zw, x + zw), y),
                    'left': (max(0, x - zw), y),
                    'top': (x, max(0, y - zh)),
                    'bottom': (x, min(gh - zh, y + zh)),
                }[s]
            @staticmethod
            def _dynamic_offsets():
                return {'lobby': (lob_x, lob_y), 'dungeon': (dun_x, dun_y)}

        monkeypatch.setattr(ms, 'global_map_settings', Settings, raising=True)
        m = ms.MapService().build_map()
        # Basic invariants
        assert len(m.matrix) == Settings.global_height
        assert all(len(r) == Settings.global_width for r in m.matrix)
