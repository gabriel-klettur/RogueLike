import random
import types
import roguelike_engine.map.view.map_view as mv


def test_view_fuzz_inputs_no_crash(monkeypatch):
    rng = random.Random(9090)

    # Fake pygame + ZoneView
    class FakePygame:
        class Rect:
            def __init__(self, topleft, size):
                self.topleft = topleft
                self.size = size
        class Surface:
            pass
    monkeypatch.setattr(mv, 'pygame', FakePygame, raising=True)

    class StubZoneView:
        def render_zone(self, screen, camera, zone_name, tiles):
            return None
    monkeypatch.setattr(mv, 'ZoneView', StubZoneView, raising=True)

    # Randomized TILE_SIZE in a small range
    monkeypatch.setattr(mv, 'TILE_SIZE', rng.choice([8, 16, 24, 32]), raising=True)

    view = mv.MapView()

    for _ in range(10):
        # Random camera that may hide/show tiles
        visibility = rng.random()
        camera = types.SimpleNamespace(
            is_in_view=lambda x, y, size, v=visibility: (x + y) % 100 < int(v * 100),
            apply=lambda pos: (pos[0], pos[1]),
        )
        # Random tiles and zones
        zones = {}
        for z in range(rng.randint(1, 3)):
            zone_name = f"Z{z}"
            tiles = [types.SimpleNamespace(x=rng.randint(0, 128), y=rng.randint(0, 128)) for _ in range(rng.randint(0, 5))]
            zones[zone_name] = tiles
        map_manager = types.SimpleNamespace(tiles_by_zone=zones)
        screen = FakePygame.Surface()

        # Should not raise
        dirty = view.render(screen, camera, map_manager)
        assert isinstance(dirty, list)
