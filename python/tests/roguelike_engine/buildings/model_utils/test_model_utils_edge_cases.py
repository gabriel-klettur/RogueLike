import pygame

from roguelike_engine.buildings.model_utils.image_ops import load_and_prepare_image


def test_image_loader_downscale_large_when_no_explicit_scale(monkeypatch, pygame_init):
    def fake_loader(_path: str) -> pygame.Surface:
        return pygame.Surface((1024, 256), flags=pygame.SRCALPHA)

    s, size = load_and_prepare_image("big.png", None, loader=fake_loader)
    assert size == (256, 64)
    assert s.get_size() == (256, 64)


def test_image_loader_no_downscale_small(monkeypatch, pygame_init):
    def fake_loader(_path: str) -> pygame.Surface:
        return pygame.Surface((128, 128), flags=pygame.SRCALPHA)

    s, size = load_and_prepare_image("small.png", None, loader=fake_loader)
    assert size == (128, 128)
    assert s.get_size() == (128, 128)
