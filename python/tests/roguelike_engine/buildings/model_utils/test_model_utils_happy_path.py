import pygame

from roguelike_engine.buildings.model_utils.image_ops import load_and_prepare_image
from roguelike_engine.buildings.model_utils.collision_ops import build_collision_tiles, build_collision_tile_objs


def test_image_loader_applies_scale_and_cache(monkeypatch, pygame_init):
    calls = {"n": 0}

    def fake_loader(_path: str) -> pygame.Surface:
        calls["n"] += 1
        return pygame.Surface((100, 50), flags=pygame.SRCALPHA)

    s1, size1 = load_and_prepare_image("a.png", (20, 10), loader=fake_loader)
    s2, size2 = load_and_prepare_image("a.png", (20, 10), loader=fake_loader)
    assert size1 == (20, 10) and size2 == (20, 10)
    assert s1 is s2  # cached
    assert calls["n"] == 1  # loaded once


def test_collision_ops_builders(monkeypatch, pygame_init):
    rects = build_collision_tiles([["#", "."], [".", "#"]], base_x=5, base_y=7, tile_size=16)
    assert [r.topleft for r in rects] == [(5, 7), (5 + 16, 7 + 16)]
    objs = build_collision_tile_objs(rects)
    assert all(getattr(o, "solid", False) for o in objs)
    assert [o.rect.topleft for o in objs] == [r.topleft for r in rects]
