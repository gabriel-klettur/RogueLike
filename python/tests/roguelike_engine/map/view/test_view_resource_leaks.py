import types
import roguelike_engine.map.view.map_view as mv


def test_render_does_not_mutate_map_manager_tiles(monkeypatch):
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
            pass
    monkeypatch.setattr(mv, 'ZoneView', StubZoneView, raising=True)

    # Build view and stubs
    view = mv.MapView()
    camera = types.SimpleNamespace(
        is_in_view=lambda x, y, size: True,
        apply=lambda pos: pos,
    )
    tile = types.SimpleNamespace(x=0, y=0)
    tiles_by_zone = {'A': [tile, tile], 'B': [tile]}
    map_manager = types.SimpleNamespace(tiles_by_zone=tiles_by_zone)

    screen = FakePygame.Surface()

    # Call render multiple times and ensure map_manager.tiles_by_zone is unmodified
    before_ids = {k: id(v) for k, v in tiles_by_zone.items()}
    for _ in range(3):
        dirty = view.render(screen, camera, map_manager)
        assert isinstance(dirty, list)
        assert {k: id(v) for k, v in tiles_by_zone.items()} == before_ids
        # And lengths stable
        assert {k: len(v) for k, v in tiles_by_zone.items()} == {'A': 2, 'B': 1}
