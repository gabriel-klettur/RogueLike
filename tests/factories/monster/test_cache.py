import pytest
from roguelike_game.factories.monster.cache import load_caches_for, _SPRITE_SURFACES, _DEATH_SURFACES, _loaded_variants
import roguelike_game.factories.monster.config as config_mod

class DummySurf:
    def __init__(self, size=(5,5)):
        self._size = size
    def get_size(self):
        return self._size
    def fill(self, *args, **kwargs):
        pass

@pytest.fixture(autouse=True)
def patch_dependencies(monkeypatch):
    # Set up a dummy monster definition
    config_mod.MONSTER_DEFS.clear()
    config_mod.MONSTER_DEFS['dummy'] = {
        'scale':1.0,
        'sprites': {'down':'path1'},
        'death_sprite':'path2',
        'death_scale':1.0,
        'tint': None
    }
    import roguelike_game.factories.monster.cache as cache_mod
    monkeypatch.setattr(cache_mod, 'load_image', lambda path: DummySurf())
    monkeypatch.setattr(cache_mod.pygame.transform, 'scale', lambda surf, size: surf)


def test_load_caches_for():
    _SPRITE_SURFACES.clear()
    _DEATH_SURFACES.clear()
    _loaded_variants.clear()
    load_caches_for(['dummy'])
    assert 'dummy' in _SPRITE_SURFACES
    assert 'dummy' in _DEATH_SURFACES
    # Calling again shouldn't reload
    prev_loaded = set(_loaded_variants)
    load_caches_for(['dummy'])
    assert set(_loaded_variants) == prev_loaded
