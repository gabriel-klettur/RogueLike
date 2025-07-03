import pytest
from roguelike_game.factories.monster.physics import (
    calculate_position, create_physics_components,
    create_collider_components, create_zlayer_component
)
from roguelike_engine.config.config_tiles import TILE_SIZE
import roguelike_engine.config.config_z_layer as z_mod

class DummySurf:
    def __init__(self, size):
        self._size = size
    def get_size(self):
        return self._size

class DummyMask:
    pass

@pytest.fixture(autouse=True)
def patch_pygame(monkeypatch):
    import roguelike_game.factories.monster.physics as phys
    monkeypatch.setattr(phys.pygame.mask, 'from_surface', lambda surf: DummyMask())
    monkeypatch.setattr(phys.pygame.transform, 'scale', lambda surf, size: surf)


def test_calculate_position():
    class DummySprite:
        image = DummySurf((20, 40))
    cfg = {'scale':1.0}
    px, py = calculate_position(2, 3, cfg, DummySprite())
    assert px == 2 * TILE_SIZE + TILE_SIZE//2 - 20//2
    assert py == (3+1) * TILE_SIZE - 40 - 1


def test_create_physics_components():
    scale, vel = create_physics_components({'scale':2.5})
    assert scale.scale == 2.5
    assert vel.vx == 0 and vel.vy == 0


def test_create_collider_components():
    class DummySprite:
        def __init__(self):
            self.image = DummySurf((10, 20))
    comp = create_collider_components(DummySprite(), {'scale':1.0, 'feet_width_factor':0.5, 'feet_height_factor':0.2})
    assert set(comp.colliders.keys()) == {'body', 'feet'}


def test_create_zlayer_component():
    z = create_zlayer_component({'faction':'ENEMY'})
    assert z.layer == z_mod.Z_LAYERS.get('monster', 0)
