import pygame
import pytest
from roguelike_game.factories.player import loader

class FakeSurface:
    def __init__(self, w, h):
        self._w = w; self._h = h
    def get_width(self): return self._w
    def get_height(self): return self._h
    def get_size(self): return (self._w, self._h)

class DummyAssets:
    def __init__(self, class_player, size): pass
    def get_sprites(self):
        sprites = {
            'down': {'idle': [FakeSurface(2,4)], 'walk': [FakeSurface(2,4), FakeSurface(2,4)]},
            'up': {'idle': [], 'walk': []},
        }
        return sprites, None

@pytest.fixture(autouse=True)
def patch_assets(monkeypatch):
    monkeypatch.setattr(loader, 'PlayerAssets', DummyAssets)


def test_extract_initial_frame():
    sprites = {'down': {'idle': [1,2]}, 'left': {'idle': []}}
    assert loader.extract_initial_frame(sprites) == 1
    assert loader.extract_initial_frame({'down': {'idle': []}}) is None


def test_build_animator_map():
    sprites = {'down': {'idle': [1], 'walk': [2]}, 'left': {'idle': [3], 'walk': [4]}}
    anim = loader.build_animator_map(sprites)
    assert anim['down_idle'] == [1]
    assert anim['down_walk'] == [2]
    assert anim['left_idle'] == [3]
    assert anim['left_walk'] == [4]


def test_load_and_scale_sprites_no_scale(monkeypatch):
    # scale == DEFAULT_SCALE, no transform.scale
    monkeypatch.setitem(loader.PLAYER_STATS, 'test', {'scale': loader.DEFAULT_SCALE})
    sprites = loader.load_and_scale_sprites('test')
    assert isinstance(sprites['down']['idle'][0], FakeSurface)


def test_load_and_scale_sprites_with_scale(monkeypatch):
    # scale != DEFAULT_SCALE
    monkeypatch.setitem(loader.PLAYER_STATS, 'test', {'scale': loader.DEFAULT_SCALE * 2})
    monkeypatch.setattr(pygame.transform, 'scale', lambda frame, size: ('scaled', size))
    sprites = loader.load_and_scale_sprites('test')
    val = sprites['down']['idle'][0]
    assert val[0] == 'scaled'
    # tamaño escalado debe ser ancho*scale, alto*scale
    assert val[1] == (int(2 * loader.PLAYER_STATS['test']['scale']), int(4 * loader.PLAYER_STATS['test']['scale']))
