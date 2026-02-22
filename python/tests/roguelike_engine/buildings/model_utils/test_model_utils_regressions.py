import pygame

from roguelike_engine.buildings.model_utils.image_ops import load_and_prepare_image


def test_image_cache_key_differs_by_scale(monkeypatch, pygame_init):
    def fake_loader(_path: str) -> pygame.Surface:
        return pygame.Surface((100, 50), flags=pygame.SRCALPHA)

    s1, size1 = load_and_prepare_image("a.png", (50, 25), loader=fake_loader)
    s2, size2 = load_and_prepare_image("a.png", (25, 25), loader=fake_loader)
    assert size1 != size2 and s1 is not s2
