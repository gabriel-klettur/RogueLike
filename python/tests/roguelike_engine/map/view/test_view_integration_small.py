import types
import pygame

import roguelike_engine.map.view.map_view as mv


def test_map_view_integration_dirty_rects_are_screen_space(monkeypatch):
    # Fake ZoneView to avoid external deps
    class DummyZoneView:
        def render_zone(self, screen, camera, zone_name, tiles):
            # draw a dot at the camera-applied position of first tile (if any)
            if tiles:
                x, y = camera.apply((tiles[0].x, tiles[0].y))
                pygame.draw.rect(screen, (255, 0, 0), pygame.Rect(x, y, 2, 2))
    monkeypatch.setattr(mv, 'ZoneView', DummyZoneView, raising=True)

    view = mv.MapView()

    # Camera applies an offset translation; is_in_view always True
    camera = types.SimpleNamespace(
        is_in_view=lambda x, y, size: True,
        apply=lambda pos: (pos[0] + 5, pos[1] + 7),
    )

    # One small zone with two tiles
    Tile = types.SimpleNamespace
    tiles_by_zone = {
        'Z': [Tile(x=0, y=0), Tile(x=16, y=0)],
    }
    map_manager = types.SimpleNamespace(tiles_by_zone=tiles_by_zone)
    screen = pygame.Surface((64, 64), flags=pygame.SRCALPHA)

    dirty = view.render(screen, camera, map_manager)

    # Dirty rects should be pygame.Rect within the screen bounds
    assert dirty and all(isinstance(r, pygame.Rect) for r in dirty)
    for r in dirty:
        assert r.right <= screen.get_width() and r.bottom <= screen.get_height()
